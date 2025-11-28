using CMS.Application.DTOs.System;
using CMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;

namespace CMS.Api.Filters;

[AttributeUsage(AttributeTargets.Method)]
public class CachedAttribute : Attribute, IAsyncActionFilter
{
    private readonly int _ttlSeconds;
    private readonly string _baseTag;

    // Redis (Server) Configuration
    public bool IsShared { get; set; } = false;

    // Browser (Client) Configuration
    // Set to TRUE if you want the browser to store data. 
    // Set to FALSE (Default) to force the browser to always ask the server.
    public bool AllowClientCache { get; set; } = false;

    // Concurrency
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public CachedAttribute(int ttlSeconds, string baseTag)
    {
        _ttlSeconds = ttlSeconds;
        _baseTag = baseTag;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (context.HttpContext.Request.Method != "GET")
        {
            await next();
            return;
        }

        var services = context.HttpContext.RequestServices;
        var cacheService = services.GetRequiredService<ICacheService>();

        var userId = IsShared ? null : context.HttpContext.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var cacheKey = GenerateKey(context.HttpContext.Request, userId);

        // 1. FAST PATH: Try Read Redis
        var cachedWrapper = await cacheService.GetAsync<CacheWrapper>(cacheKey);

        if (cachedWrapper != null)
        {
            // Even if client cache is off, we can still check ETag (304) to save bandwidth
            // if the browser happened to hold onto the ETag.
            if (TryServe304(context, cachedWrapper.Content)) return;

            ServeResponse(context, cachedWrapper);
            return;
        }

        // 2. SLOW PATH: Lock and DB
        var myLock = _locks.GetOrAdd(cacheKey, k => new SemaphoreSlim(1, 1));

        if (await myLock.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            try
            {
                cachedWrapper = await cacheService.GetAsync<CacheWrapper>(cacheKey);
                if (cachedWrapper != null)
                {
                    ServeResponse(context, cachedWrapper);
                    return;
                }

                var executedContext = await next();

                if (executedContext.Result is OkObjectResult okResult)
                {
                    var jsonResponse = System.Text.Json.JsonSerializer.Serialize(okResult.Value);
                    var wrapper = new CacheWrapper
                    {
                        Content = jsonResponse,
                        ContentType = "application/json"
                    };

                    var tags = new List<string> { _baseTag };
                    if (!IsShared && !string.IsNullOrEmpty(userId)) tags.Add($"{_baseTag}_user_{userId}");
                    if (context.HttpContext.Request.RouteValues.TryGetValue("id", out var idVal)) tags.Add($"{_baseTag}_id_{idVal}");

                    await cacheService.SetAsync(cacheKey, wrapper, TimeSpan.FromSeconds(_ttlSeconds), tags.ToArray());

                    // IMPORTANT: Serve the response correctly with headers
                    // We must manually set the ETag here for the first response
                    ServeResponse(executedContext, wrapper);
                }
            }
            finally
            {
                myLock.Release();
                _locks.TryRemove(cacheKey, out _);
            }
        }
        else
        {
            await next();
        }
    }

    // --- UPDATED HELPERS ---

    private void ServeResponse(ActionContext context, CacheWrapper wrapper)
    {
        var response = context.HttpContext.Response;

        // Always calculate ETag (It allows for 304 Not Modified even if no-cache is set)
        var etag = GenerateETag(wrapper.Content);
        response.Headers.ETag = etag;

        if (AllowClientCache)
        {
            // Browser can store data for _ttlSeconds
            response.Headers.CacheControl = $"public,max-age={_ttlSeconds}";
        }
        else
        {
            // Browser MUST NOT store data. Must check with server every time.
            // "no-cache" means: You can store it, but you must validate ETag with server before showing it.
            // "no-store" means: Don't store it at all.
            response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            response.Headers.Pragma = "no-cache";
            response.Headers.Expires = "0";
        }

        // If we are modifying the ExecutedContext (initial load), we don't need to replace the Result.
        // If we are in the caching hit (ActionExecutingContext), we do.
        if (context is ActionExecutingContext executingContext)
        {
            executingContext.Result = new ContentResult
            {
                Content = wrapper.Content,
                ContentType = wrapper.ContentType,
                StatusCode = 200
            };
        }
    }

    private bool TryServe304(ActionExecutingContext context, string content)
    {
        var etag = GenerateETag(content);
        if (context.HttpContext.Request.Headers.TryGetValue("If-None-Match", out var incomingEtag) &&
            incomingEtag.ToString() == etag)
        {
            context.Result = new StatusCodeResult(304);
            return true;
        }
        return false;
    }

    private static string GenerateKey(HttpRequest request, string? userId)
    {
        var keyBuilder = new StringBuilder();
        keyBuilder.Append($"{request.Path}");
        foreach (var (key, value) in request.Query.OrderBy(x => x.Key))
        {
            keyBuilder.Append($"|{key}-{value}");
        }
        if (userId != null) keyBuilder.Append($"|u-{userId}");
        return keyBuilder.ToString();
    }

    private static string GenerateETag(string content)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha.ComputeHash(bytes);
        return $"\"{Convert.ToBase64String(hash)}\"";
    }
}