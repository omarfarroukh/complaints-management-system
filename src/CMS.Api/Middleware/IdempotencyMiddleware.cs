using System;
using System.IO;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using CMS.Api.Filters;
using CMS.Application.Wrappers;
using StackExchange.Redis;

namespace CMS.Api.Middleware
{
    /// <summary>
    /// Middleware that provides idempotency support by caching successful responses keyed by an Idempotency-Key header.
    /// It now supports a configurable maximum response size and optional GZip compression to reduce Redis storage.
    /// Includes distributed locking to prevent race conditions.
    /// </summary>
    public class IdempotencyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IDistributedCache _cache;
        private readonly ILogger<IdempotencyMiddleware> _logger;
        private readonly IConfiguration _configuration;

        public IdempotencyMiddleware(
            RequestDelegate next, 
            IDistributedCache cache, 
            ILogger<IdempotencyMiddleware> logger, 
            IConfiguration configuration)
        {
            _next = next ?? throw new ArgumentNullException(nameof(next));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Retrieve the attribute that marks an endpoint as idempotent.
            var endpoint = context.GetEndpoint();
            var idempotencyAttribute = endpoint?.Metadata.GetMetadata<IdempotencyAttribute>();

            if (idempotencyAttribute == null)
            {
                await _next(context);
                return;
            }

            // Only apply to non-idempotent HTTP methods (POST, PATCH, PUT)
            if (!IsNonIdempotentMethod(context.Request.Method))
            {
                await _next(context);
                return;
            }

            // Ensure the Idempotency-Key header is present.
            if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey) 
                || string.IsNullOrWhiteSpace(idempotencyKey))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new ApiResponse<string>
                {
                    Succeeded = false,
                    Message = "Idempotency-Key header is required for this operation"
                });
                return;
            }

            // Build a cache key that is optionally scoped to the authenticated user.
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var cacheKey = string.IsNullOrEmpty(userId)
                ? $"Idempotency:{idempotencyKey}"
                : $"Idempotency:{userId}:{idempotencyKey}";

            var lockKey = $"{cacheKey}:lock";

            // Load configuration values (with sensible defaults).
            var maxResponseSizeKb = _configuration.GetValue<int>("Idempotency:MaxResponseSizeKb", 1024); // default 1 MB
            var enableCompression = _configuration.GetValue<bool>("Idempotency:EnableCompression", true);
            var storeFullResponse = _configuration.GetValue<bool>("Idempotency:StoreFullResponse", true);
            var lockTimeoutSeconds = _configuration.GetValue<int>("Idempotency:LockTimeoutSeconds", 30);

            // Get Redis connection for distributed locking
            var redis = context.RequestServices.GetService<IConnectionMultiplexer>();
            if (redis == null)
            {
                _logger.LogWarning("Redis connection not available for idempotency locking");
                await _next(context);
                return;
            }

            var db = redis.GetDatabase();

            // Try to acquire distributed lock to prevent race conditions
            var lockAcquired = await db.StringSetAsync(
                lockKey, 
                "locked", 
                TimeSpan.FromSeconds(lockTimeoutSeconds), 
                When.NotExists);

            if (!lockAcquired)
            {
                // Another request is currently processing this idempotency key
                _logger.LogWarning(
                    "Concurrent request detected for idempotency key: {IdempotencyKey}", 
                    idempotencyKey.ToString());
                
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new ApiResponse<string>
                {
                    Succeeded = false,
                    Message = "This request is currently being processed. Please wait."
                });
                return;
            }

            try
            {
                // Try to serve a cached response.
                var cachedResponse = await _cache.GetStringAsync(cacheKey);
                if (!string.IsNullOrEmpty(cachedResponse))
                {
                    IdempotencyResponse? responseData = null;
                    try
                    {
                        if (enableCompression)
                        {
                            var compressedBytes = Convert.FromBase64String(cachedResponse);
                            using var ms = new MemoryStream(compressedBytes);
                            using var gzip = new GZipStream(ms, CompressionMode.Decompress);
                            using var reader = new StreamReader(gzip);
                            var decompressed = await reader.ReadToEndAsync();
                            responseData = JsonSerializer.Deserialize<IdempotencyResponse>(decompressed);
                        }
                        else
                        {
                            responseData = JsonSerializer.Deserialize<IdempotencyResponse>(cachedResponse);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex, 
                            "Failed to deserialize cached idempotent response for key {IdempotencyKey}", 
                            idempotencyKey.ToString());
                    }

                    if (responseData != null)
                    {
                        _logger.LogInformation(
                            "Returning cached idempotent response for key {IdempotencyKey}", 
                            idempotencyKey.ToString());
                        
                        context.Response.StatusCode = responseData.StatusCode;
                        context.Response.ContentType = responseData.ContentType;
                        await context.Response.WriteAsync(responseData.Body);
                        return;
                    }
                }

                // No cached response – capture the outgoing response.
                var originalBodyStream = context.Response.Body;
                await using var memoryStream = new MemoryStream();
                context.Response.Body = memoryStream;

                await _next(context);

                // Read the response body.
                memoryStream.Position = 0;
                var responseBody = await new StreamReader(memoryStream).ReadToEndAsync();
                
                // Reset the stream position and copy back to the original response body.
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(originalBodyStream);
                context.Response.Body = originalBodyStream;

                // Cache only successful (2xx) responses.
                if (context.Response.StatusCode >= 200 && context.Response.StatusCode < 300)
                {
                    var responseSizeBytes = Encoding.UTF8.GetByteCount(responseBody);
                    if (responseSizeBytes > maxResponseSizeKb * 1024)
                    {
                        _logger.LogWarning(
                            "Idempotency response for key {IdempotencyKey} exceeds max size of {MaxKb}KB ({ActualKb}KB); skipping cache.", 
                            idempotencyKey, 
                            maxResponseSizeKb,
                            responseSizeBytes / 1024);
                    }
                    else
                    {
                        var responseToCache = new IdempotencyResponse
                        {
                            StatusCode = context.Response.StatusCode,
                            ContentType = context.Response.ContentType ?? "application/json",
                            Body = storeFullResponse ? responseBody : string.Empty,
                            IsCompressed = enableCompression
                        };

                        var serialized = JsonSerializer.Serialize(responseToCache);
                        if (enableCompression)
                        {
                            using var ms = new MemoryStream();
                            using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                            {
                                var bytes = Encoding.UTF8.GetBytes(serialized);
                                gzip.Write(bytes, 0, bytes.Length);
                            }
                            var compressedBytes = ms.ToArray();
                            serialized = Convert.ToBase64String(compressedBytes);
                        }

                        var cacheOptions = new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(idempotencyAttribute.CacheDurationMinutes)
                        };

                        await _cache.SetStringAsync(cacheKey, serialized, cacheOptions);
                        
                        _logger.LogInformation(
                            "Cached idempotent response for key {IdempotencyKey} ({SizeKb}KB)", 
                            idempotencyKey,
                            responseSizeBytes / 1024);
                    }
                }
            }
            finally
            {
                // Release the distributed lock
                await db.KeyDeleteAsync(lockKey);
            }
        }

        /// <summary>
        /// Determines if the HTTP method is non-idempotent and should use idempotency keys
        /// </summary>
        private bool IsNonIdempotentMethod(string method)
        {
            return method == "POST" || method == "PATCH" || method == "PUT";
        }

        private class IdempotencyResponse
        {
            public int StatusCode { get; set; }
            public string ContentType { get; set; } = string.Empty;
            public string Body { get; set; } = string.Empty;
            public bool IsCompressed { get; set; }
        }
    }
}