using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using StackExchange.Redis;

namespace CMS.Api.Filters;

[AttributeUsage(AttributeTargets.Method)]
public class InvalidateCacheAttribute : Attribute, IAsyncActionFilter
{
    private readonly string[] _tags;

    public InvalidateCacheAttribute(params string[] tags)
    {
        _tags = tags ?? throw new ArgumentNullException(nameof(tags));
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // 1. Run the Action first (Create/Update/Delete)
        var executedContext = await next();

        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<InvalidateCacheAttribute>>();

        // 2. If successful, invalidate the cache
        if (executedContext.Exception == null && IsSuccessResult(executedContext.Result))
        {
            try
            {
                var redis = context.HttpContext.RequestServices.GetRequiredService<IConnectionMultiplexer>();
                var db = redis.GetDatabase();

                var totalKeysInvalidated = 0;

                foreach (var tag in _tags)
                {
                    // A. Find all keys related to this tag
                    var keys = await db.SetMembersAsync($"tag:{tag}");
                    
                    if (keys.Length > 0)
                    {
                        // B. Convert RedisValue[] to RedisKey[]
                        var redisKeys = Array.ConvertAll(keys, k => (RedisKey)k.ToString());
                        
                        // C. Delete the actual cached data
                        await db.KeyDeleteAsync(redisKeys);
                        
                        // D. Delete the Tag Set itself
                        await db.KeyDeleteAsync($"tag:{tag}");
                        
                        totalKeysInvalidated += keys.Length;
                        
                        logger.LogInformation(
                            "🗑️ Invalidated {Count} cache entries for tag: {Tag}", 
                            keys.Length, 
                            tag);
                    }
                    else
                    {
                        logger.LogDebug("No cache entries found for tag: {Tag}", tag);
                    }
                }

                if (totalKeysInvalidated > 0)
                {
                    logger.LogInformation(
                        "✅ Cache invalidation complete: {TotalKeys} entries removed for tags: {Tags}",
                        totalKeysInvalidated,
                        string.Join(", ", _tags));
                }
            }
            catch (Exception ex)
            {
                // Log but don't throw - cache invalidation failure shouldn't break the request
                logger.LogWarning(
                    ex, 
                    "⚠️ Cache invalidation failed for tags: {Tags}. Cache may be stale.", 
                    string.Join(", ", _tags));
            }
        }
        else if (executedContext.Exception != null)
        {
            logger.LogDebug(
                "Skipping cache invalidation due to exception in action for tags: {Tags}", 
                string.Join(", ", _tags));
        }
    }

    /// <summary>
    /// Determines if the action result indicates a successful operation
    /// </summary>
    private bool IsSuccessResult(IActionResult? result)
    {
        return result is OkResult 
            or OkObjectResult 
            or CreatedAtActionResult 
            or CreatedAtRouteResult
            or NoContentResult;
    }
}