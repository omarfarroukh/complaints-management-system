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
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using CMS.Api.Filters;

namespace CMS.Api.Middleware
{
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
            var endpoint = context.GetEndpoint();
            var idempotencyAttribute = endpoint?.Metadata.GetMetadata<IdempotencyAttribute>();

            if (idempotencyAttribute == null)
            {
                await _next(context);
                return;
            }

            if (!IsNonIdempotentMethod(context.Request.Method))
            {
                await _next(context);
                return;
            }

            if (!context.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKey) 
                || string.IsNullOrWhiteSpace(idempotencyKey))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new { succeeded = false, message = "Idempotency-Key header is required" });
                return;
            }

            // Compute request payload hash
            string requestPayloadHash = await ComputeRequestHashAsync(context.Request);

            // Build cache key
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var cacheKey = string.IsNullOrEmpty(userId)
                ? $"Idempotency:{idempotencyKey}"
                : $"Idempotency:{userId}:{idempotencyKey}";
            
            var lockKey = $"{cacheKey}:lock";

            // Load configuration
            var maxResponseSizeKb = _configuration.GetValue<int>("Idempotency:MaxResponseSizeKb", 1024);
            var enableCompression = _configuration.GetValue<bool>("Idempotency:EnableCompression", true);
            var storeFullResponse = _configuration.GetValue<bool>("Idempotency:StoreFullResponse", true);
            var lockTimeoutSeconds = _configuration.GetValue<int>("Idempotency:LockTimeoutSeconds", 30);

            // Acquire distributed lock
            var redis = context.RequestServices.GetService<IConnectionMultiplexer>();
            if (redis == null)
            {
                _logger.LogWarning("Redis not available for idempotency locking");
                await _next(context);
                return;
            }

            var db = redis.GetDatabase();
            var lockAcquired = await db.StringSetAsync(lockKey, "locked", TimeSpan.FromSeconds(lockTimeoutSeconds), When.NotExists);

            if (!lockAcquired)
            {
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                await context.Response.WriteAsJsonAsync(new { succeeded = false, message = "Request already being processed" });
                return;
            }

            try
            {
                // In the try block, replace the cached response handling:

                var cachedValue = await _cache.GetStringAsync(cacheKey);
                if (!string.IsNullOrEmpty(cachedValue))
                {
                    var cacheEntry = DeserializeCacheEntry(cachedValue, enableCompression);
                    
                    // Check if cache entry exists and hash matches
                    if (cacheEntry?.RequestPayloadHash == requestPayloadHash)
                    {
                        // Null-forgiving operator is safe here - a valid entry must have Response
                        context.Response.StatusCode = cacheEntry.Response!.StatusCode;
                        context.Response.ContentType = cacheEntry.Response.ContentType;
                        await context.Response.WriteAsync(cacheEntry.Response.Body);
                        return;
                    }
                    else if (cacheEntry != null)
                    {
                        // Entry exists but hash mismatch
                        context.Response.StatusCode = StatusCodes.Status409Conflict;
                        await context.Response.WriteAsJsonAsync(new 
                        { 
                            succeeded = false, 
                            message = "Idempotency key used with different request payload" 
                        });
                        return;
                    }
                    // If cacheEntry is null (corrupted), fall through to process request
                }

                // Process request and capture response
                var originalBodyStream = context.Response.Body;
                await using var memoryStream = new MemoryStream();
                context.Response.Body = memoryStream;

                await _next(context);

                memoryStream.Position = 0;
                var responseBody = await new StreamReader(memoryStream).ReadToEndAsync();
                
                memoryStream.Position = 0;
                await memoryStream.CopyToAsync(originalBodyStream);
                context.Response.Body = originalBodyStream;

                // Cache successful responses
                if (IsSuccessStatusCode(context.Response.StatusCode))
                {
                    var responseSizeBytes = Encoding.UTF8.GetByteCount(responseBody);
                    if (responseSizeBytes > maxResponseSizeKb * 1024)
                    {
                        _logger.LogWarning("Response too large for idempotency cache: {Size}KB", responseSizeBytes / 1024);
                    }
                    else
                    {
                        var cacheEntry = new IdempotencyCacheEntry
                        {
                            RequestPayloadHash = requestPayloadHash,
                            Response = new IdempotencyResponse
                            {
                                StatusCode = context.Response.StatusCode,
                                ContentType = context.Response.ContentType ?? "application/json",
                                Body = storeFullResponse ? responseBody : string.Empty,
                                IsCompressed = enableCompression
                            }
                        };

                        var serialized = JsonSerializer.Serialize(cacheEntry);
                        if (enableCompression)
                        {
                            serialized = CompressString(serialized);
                        }

                        var cacheOptions = new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(idempotencyAttribute.CacheDurationMinutes)
                        };

                        await _cache.SetStringAsync(cacheKey, serialized, cacheOptions);
                    }
                }
            }
            finally
            {
                await db.KeyDeleteAsync(lockKey);
            }
        }

        private async Task<string> ComputeRequestHashAsync(HttpRequest request)
        {
            if (request.ContentLength == 0 || !request.Body.CanRead)
            {
                return "empty_body";
            }

            request.EnableBuffering();
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            request.Body.Position = 0;

            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(body));
            return Convert.ToBase64String(bytes);
        }

        private string CompressString(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            using var ms = new MemoryStream();
            using (var gzip = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }
            return Convert.ToBase64String(ms.ToArray());
        }

        // FIX: Made return type nullable and added null checks
        private IdempotencyCacheEntry? DeserializeCacheEntry(string value, bool isCompressed)
        {
            try
            {
                string json;
                if (isCompressed)
                {
                    var compressedBytes = Convert.FromBase64String(value);
                    using var ms = new MemoryStream(compressedBytes);
                    using var gzip = new GZipStream(ms, CompressionMode.Decompress);
                    using var reader = new StreamReader(gzip);
                    json = reader.ReadToEnd();
                }
                else
                {
                    json = value;
                }

                // FIX: Handle potential null result from deserialization
                return JsonSerializer.Deserialize<IdempotencyCacheEntry>(json) ?? null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize idempotency cache entry");
                return null;
            }
        }

        private bool IsNonIdempotentMethod(string method) => 
            method == HttpMethods.Post || method == HttpMethods.Patch || method == HttpMethods.Put;

        private bool IsSuccessStatusCode(int statusCode) => 
            statusCode >= StatusCodes.Status200OK && statusCode < StatusCodes.Status300MultipleChoices;

        // FIX: Made properties nullable to satisfy null reference warnings
        private class IdempotencyCacheEntry
        {
            public string? RequestPayloadHash { get; set; }
            public IdempotencyResponse? Response { get; set; }
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