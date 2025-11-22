using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace CMS.Api.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class CachedAttribute : Attribute, IAsyncActionFilter
{
    private readonly int _timeToLiveSeconds;
    private readonly string[] _tags;
    
    /// <summary>
    /// Toggle between User-Specific (false) vs Shared (true) caching
    /// </summary>
    public bool IsShared { get; set; } = false;

    public CachedAttribute(int timeToLiveSeconds, params string[] tags)
    {
        _timeToLiveSeconds = timeToLiveSeconds;
        _tags = tags;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // 1. Get Services
        var cache = context.HttpContext.RequestServices.GetRequiredService<IDistributedCache>();
        var redis = context.HttpContext.RequestServices.GetRequiredService<IConnectionMultiplexer>();
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<CachedAttribute>>();
        var env = context.HttpContext.RequestServices.GetRequiredService<IHostEnvironment>();

        var key = GenerateCacheKeyFromContext(context.HttpContext, IsShared);

        // 2. TRY TO READ CACHE (Safely)
        try
        {
            var cachedResponse = await cache.GetStringAsync(key);
            if (!string.IsNullOrEmpty(cachedResponse))
            {
                logger.LogDebug("✅ Cache HIT for key: {Key}", key);
                
                context.Result = new ContentResult
                {
                    Content = cachedResponse,
                    ContentType = "application/json",
                    StatusCode = 200
                };
                return;
            }
            
            logger.LogDebug("⚠️ Cache MISS for key: {Key}", key);
        }
        catch (Exception ex)
        {
            // REDIS IS DOWN: Log warning, but DO NOT throw. 
            // Let the request proceed to the database.
            logger.LogWarning(ex, "Redis cache read failed for key {Key}. Falling back to database.", key);
            
            // In development, make it more visible
            if (env.IsDevelopment())
            {
                logger.LogError("🔴 Redis connectivity issue detected! Check your Redis server.");
            }
        }

        // 3. EXECUTE CONTROLLER (Database hit)
        var executedContext = await next();

        // 4. TRY TO WRITE CACHE (Safely)
        if (executedContext.Result is OkObjectResult okObjectResult)
        {
            try
            {
                var jsonResponse = System.Text.Json.JsonSerializer.Serialize(okObjectResult.Value);
                
                await cache.SetStringAsync(key, jsonResponse, new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_timeToLiveSeconds)
                });

                logger.LogDebug("💾 Cached response for key: {Key} (TTL: {TTL}s)", key, _timeToLiveSeconds);

                // Store tags for invalidation
                if (_tags.Length > 0)
                {
                    var db = redis.GetDatabase();
                    foreach (var tag in _tags)
                    {
                        await db.SetAddAsync($"tag:{tag}", key);
                        await db.KeyExpireAsync($"tag:{tag}", TimeSpan.FromSeconds(_timeToLiveSeconds));
                    }
                    
                    logger.LogDebug("🏷️ Tagged cache entry with: {Tags}", string.Join(", ", _tags));
                }
            }
            catch (Exception ex)
            {
                // REDIS IS DOWN: Log warning.
                // The user still got their data, so we don't crash.
                logger.LogWarning(ex, "Redis cache write failed for key {Key}.", key);
            }
        }
    }

    /// <summary>
    /// Generates a cache key from the HTTP request context
    /// </summary>
    private static string GenerateCacheKeyFromContext(HttpContext context, bool isShared)
    {
        var request = context.Request;
        var keyBuilder = new StringBuilder();

        // Start with the path
        keyBuilder.Append($"{request.Path}");

        // Add query parameters in sorted order for consistency
        foreach (var (key, value) in request.Query.OrderBy(x => x.Key))
        {
            keyBuilder.Append($"|{key}-{value}");
        }

        // ONLY append User ID if it is NOT a shared cache
        if (!isShared)
        {
            var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                keyBuilder.Append($"|user-{userId}");
            }
        }

        return keyBuilder.ToString();
    }
}