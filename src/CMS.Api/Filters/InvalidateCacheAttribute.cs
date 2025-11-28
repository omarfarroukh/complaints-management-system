using CMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace CMS.Api.Filters;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public class InvalidateCacheAttribute : Attribute, IAsyncActionFilter
{
    private readonly string _baseTag;
    public bool InvalidateOwners { get; set; } = false;
    public bool InvalidateShared { get; set; } = false;

    public InvalidateCacheAttribute(string baseTag)
    {
        _baseTag = baseTag;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executedContext = await next();

        if (executedContext.Exception != null || !IsSuccessResult(executedContext.Result))
        {
            return;
        }

        try
        {
            var cacheService = context.HttpContext.RequestServices.GetRequiredService<ICacheService>();
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<InvalidateCacheAttribute>>();

            var tagsToInvalidate = new List<string>();

            // 1. Always add the base tag (e.g., "profiles")
            tagsToInvalidate.Add(_baseTag);

            // 2. CRITICAL FIX: If a user is logged in, and this is NOT a shared invalidation,
            // invalidate their specific user tag too.
            // This covers the exact scenario of a user patching their own profile.
            if (!InvalidateShared)
            {
                var currentUserId = context.HttpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(currentUserId))
                {
                    tagsToInvalidate.Add($"{_baseTag}_user_{currentUserId}");
                }
            }

            // 3. Route Value Invalidation (e.g. complaints_id_123)
            if (context.HttpContext.Request.RouteValues.TryGetValue("id", out var idVal))
            {
                tagsToInvalidate.Add($"{_baseTag}_id_{idVal}");
            }

            // 4. Intelligent Owner Invalidation (Reflection from Result DTO)
            if (InvalidateOwners && executedContext.Result is ObjectResult objResult && objResult.Value != null)
            {
                var dto = objResult.Value;
                var type = dto.GetType();

                AddUserTagIfPresent(tagsToInvalidate, dto, type, "CitizenId");
                AddUserTagIfPresent(tagsToInvalidate, dto, type, "AssignedEmployeeId");
            }

            if (tagsToInvalidate.Count > 0)
            {
                await cacheService.RemoveByTagAsync(tagsToInvalidate.ToArray());
                logger.LogDebug("Invalidated tags: {Tags}", string.Join(", ", tagsToInvalidate));
            }
        }
        catch (Exception ex)
        {
             var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<InvalidateCacheAttribute>>();
             logger.LogWarning(ex, "Cache invalidation failed");
        }
    }

    private void AddUserTagIfPresent(List<string> tags, object dto, Type type, string propertyName)
    {
        var prop = type.GetProperty(propertyName);
        if (prop != null)
        {
            var value = prop.GetValue(dto)?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                tags.Add($"{_baseTag}_user_{value}");
            }
        }
    }

    private bool IsSuccessResult(IActionResult? result)
    {
        return result is OkResult 
            || result is OkObjectResult 
            || result is CreatedAtActionResult 
            || result is CreatedResult
            || result is NoContentResult;
    }
}