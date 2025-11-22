using CMS.Application.Interfaces;
using CMS.Application.Wrappers;
using System.Net;

namespace CMS.Api.Middleware;

public class IpBlacklistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IpBlacklistMiddleware> _logger;

    public IpBlacklistMiddleware(RequestDelegate next, ILogger<IpBlacklistMiddleware> logger)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Invoke(HttpContext context, ISecurityService securityService)
    {
        var ip = GetClientIpAddress(context);
        
        if (!string.IsNullOrEmpty(ip) && await securityService.IsIpBlockedAsync(ip))
        {
            // Log blocked access attempt for security audit
            _logger.LogWarning(
                "🚫 Blocked request from blacklisted IP: {IpAddress} attempting to access {Path}", 
                ip, 
                context.Request.Path);
            
            context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
            await context.Response.WriteAsJsonAsync(new ApiResponse<string>
            {
                Succeeded = false,
                Message = "Access denied"
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
                // Take the first IP in the chain (original client)
                return forwardedFor.Split(',')[0].Trim();
            }
        }

        // Fallback to direct connection IP
        return context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "Unknown";
    }
}