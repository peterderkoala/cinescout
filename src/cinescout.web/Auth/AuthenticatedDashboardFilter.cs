using Hangfire.Dashboard;

namespace cinescout.web.Auth;

/// <summary>
/// Admits the Hangfire dashboard to the signed-in operator only. Replaces Hangfire's default
/// local-requests-only filter, which behind Docker/NPM would admit nobody.
/// </summary>
public sealed class AuthenticatedDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        context.GetHttpContext().User.Identity?.IsAuthenticated == true;
}
