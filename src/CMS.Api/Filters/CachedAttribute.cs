using CMS.Application.DTOs.System;
using CMS.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Json; // Required

namespace CMS.Api.Filters;

[AttributeUsage(AttributeTargets.Method)]
public class CachedAttribute : Attribute, IAsyncActionFilter
{
    private readonly int _ttlSeconds;
    private readonly string _baseTag;

    public bool IsShared { get; set; } = false;
    public bool AllowClientCache { get; set; } = false;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    // ✅ ADDED: Standard options to force CamelCase
    private static readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

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
                    // ✅ FIXED: Pass _serializerOptions here
                    var jsonResponse = JsonSerializer.Serialize(okResult.Value, _serializerOptions);

                    var wrapper = new CacheWrapper
                    {
                        Content = jsonResponse,
                        ContentType = "application/json"
                    };

                    var tags = new List<string> { _baseTag };
                    if (!IsShared && !string.IsNullOrEmpty(userId)) tags.Add($"{_baseTag}_user_{userId}");
                    if (context.HttpContext.Request.RouteValues.TryGetValue("id", out var idVal)) tags.Add($"{_baseTag}_id_{idVal}");

                    await cacheService.SetAsync(cacheKey, wrapper, TimeSpan.FromSeconds(_ttlSeconds), tags.ToArray());

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

    private void ServeResponse(ActionContext context, CacheWrapper wrapper)
    {
        var response = context.HttpContext.Response;
        var etag = GenerateETag(wrapper.Content);
        response.Headers.ETag = etag;

        if (AllowClientCache)
        {
            response.Headers.CacheControl = $"public,max-age={_ttlSeconds}";
        }
        else
        {
            response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            response.Headers.Pragma = "no-cache";
            response.Headers.Expires = "0";
        }

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