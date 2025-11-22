using CMS.Application.Interfaces;
using CMS.Application.Wrappers;

namespace CMS.Api.Middleware;

public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;

    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InvokeAsync(HttpContext context, IRateLimitService rateLimitService, ISecurityService securityService)
    {
        var ipAddress = GetClientIpAddress(context);

        // Skip whitelisted IPs
        if (await rateLimitService.IsWhitelistedAsync(ipAddress))
        {
            await _next(context);
            return;
        }

        // Increment request count
        var requestCount = await rateLimitService.IncrementRequestCountAsync(ipAddress);

        // Check if rate limit exceeded and should be blacklisted
        if (await rateLimitService.ShouldBlacklistAsync(ipAddress))
        {
            var reason = $"Auto-blacklisted: Exceeded rate limit ({requestCount} requests/min)";
            
            await rateLimitService.AutoBlacklistAsync(ipAddress, reason);
            
            _logger.LogWarning(
                "⚠️ IP {IpAddress} blacklisted due to rate limit violation: {RequestCount} requests", 
                ipAddress, 
                requestCount);

            context.Response.StatusCode = 429; // Too Many Requests
            await context.Response.WriteAsJsonAsync(new ApiResponse<string>
            {
                Succeeded = false,
                Message = "Too many requests. Your IP has been temporarily blocked."
            });
            return;
        }

        await _next(context);
    }

    /// <summary>
    /// Extracts the client IP address, handling proxy scenarios with X-Forwarded-For header
    /// </summary>
    private string GetClientIpAddress(HttpContext context)
    {
        // Check X-Forwarded-For header (for proxies/load balancers)
        if (context.Request.Headers.ContainsKey("X-Forwarded-For"))
        {
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                return forwardedFor.Split(',')[0].Trim();
            }
        }

        return context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "Unknown";
    }
}