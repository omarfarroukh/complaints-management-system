using Hangfire.Dashboard;

namespace CMS.Api.Filters;

public class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        // ⚠️ SECURITY WARNING: This opens the dashboard to everyone.
        // Since this is a learning project/dev environment, this is fine.
        // In Production, you would check for Cookies or IP Whitelists here.
        return true;
    }
}