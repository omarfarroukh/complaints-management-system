using System.Diagnostics;
using System.Security.Claims;
using System.Text;
using Serilog;
using Serilog.Context;

namespace CMS.Api.Middleware;

public class RequestLogMiddleware
{
    private readonly RequestDelegate _next;
    private const int MaxBodyLogSize = 4096; // 4KB limit to avoid logging huge payloads

    public RequestLogMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task Invoke(HttpContext context)
    {
        // 1. Skip Hangfire and health check endpoints to reduce noise
        var path = context.Request.Path.ToString();
        if (path.StartsWith("/hangfire") || path.StartsWith("/health"))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        string requestBody = "(Binary/Form Data)";

        // 2. Safely read body (Only if JSON and within size limit)
        if (context.Request.ContentType != null && 
            context.Request.ContentType.Contains("application/json") &&
            context.Request.ContentLength.HasValue &&
            context.Request.ContentLength.Value < MaxBodyLogSize)
        {
            try
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(
                    context.Request.Body, 
                    Encoding.UTF8, 
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);
                
                requestBody = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to read request body for logging");
                requestBody = "(Error reading body)";
            }
        }
        else if (context.Request.ContentLength > MaxBodyLogSize)
        {
            requestBody = $"(Body too large: {context.Request.ContentLength} bytes)";
        }

        // 3. Push STATIC properties (IP, Body) that exist before Auth
        using (LogContext.PushProperty("IPAddress", context.Connection.RemoteIpAddress?.ToString()))
        using (LogContext.PushProperty("RequestBody", requestBody))
        {
            try
            {
                // 4. Run the Pipeline (Auth happens here!)
                await _next(context);
            }
            finally
            {
                sw.Stop();

                // 5. Capture User ID NOW (After Auth has finished)
                var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "Anonymous";

                // 6. Push UserID specifically for this log entry
                using (LogContext.PushProperty("UserId", userId))
                {
                    var statusCode = context.Response.StatusCode;
                    var method = context.Request.Method;
                    var requestPath = context.Request.Path;
                    var elapsed = sw.Elapsed.TotalMilliseconds;

                    // Use different log levels based on status code
                    if (statusCode >= 500)
                    {
                        Log.Error(
                            "🔴 {Method} {Path} responded {StatusCode} in {Elapsed:0.00}ms", 
                            method, requestPath, statusCode, elapsed);
                    }
                    else if (statusCode >= 400)
                    {
                        Log.Warning(
                            "⚠️ {Method} {Path} responded {StatusCode} in {Elapsed:0.00}ms", 
                            method, requestPath, statusCode, elapsed);
                    }
                    else
                    {
                        Log.Information(
                            "✅ {Method} {Path} responded {StatusCode} in {Elapsed:0.00}ms", 
                            method, requestPath, statusCode, elapsed);
                    }
                }
            }
        }
    }
}