using CMS.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CMS.Api.Filters;

/// <summary>
/// Triggers a real-time dashboard update after a successful action execution.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class NotifyDashboardAttribute : ActionFilterAttribute
{
    private readonly string _targetRole;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotifyDashboardAttribute"/> class.
    /// </summary>
    /// <param name="targetRole">The role to notify ("Admin", "Manager", or "All")</param>
    public NotifyDashboardAttribute(string targetRole = "All")
    {
        _targetRole = targetRole;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Execute the action first
        var resultContext = await next();

        // Only trigger update if action was successful (2xx status code)
        if (resultContext.Exception == null &&
            context.HttpContext.Response.StatusCode >= 200 &&
            context.HttpContext.Response.StatusCode < 300)
        {
            var sseService = context.HttpContext.RequestServices.GetService<SseService>();
            if (sseService != null)
            {
                // Fire and forget notification
                sseService.NotifyDashboardUpdate(_targetRole);
            }
        }
    }
}
