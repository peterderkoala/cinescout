using Microsoft.AspNetCore.Antiforgery;

namespace cinescout.web.Endpoints;

/// <summary>
/// The <c>/api</c> group convention ADR 0004 mandates: every non-GET request under this prefix is
/// antiforgery-validated by default via <see cref="AntiforgeryEndpointFilter"/>, so an endpoint
/// mapped later under the returned group is protected without its author doing anything special.
/// Authorization needs no equivalent call here — <c>AuthorizationOptions.FallbackPolicy</c> (set in
/// <see cref="cinescout.web.Extensions.WebServiceCollectionExtensions.AddCineScoutAuthentication"/>)
/// already covers every endpoint-routed request with no explicit metadata.
/// </summary>
public static class ApiEndpointsExtensions
{
    public static RouteGroupBuilder MapApiGroup(this WebApplication app)
    {
        var group = app.MapGroup("/api");
        group.AddEndpointFilter<AntiforgeryEndpointFilter>();

        return group;
    }

    /// <summary>
    /// The worked example this ticket's acceptance criteria need: a real endpoint under the group
    /// to prove the antiforgery convention end-to-end (see <c>cinescout.web.Tests.ApiHardeningTests</c>)
    /// and for <c>cinescout.web.Client.Api.PingApiClient</c> to demonstrate the shared-<c>HttpClient</c>
    /// + antiforgery-header convention against. Not a real screen endpoint — #92–#98 add those.
    /// </summary>
    public static RouteGroupBuilder MapPingEndpoint(this RouteGroupBuilder apiGroup)
    {
        apiGroup.MapPost("/ping", () => Results.NoContent());

        return apiGroup;
    }
}

/// <summary>
/// Validates every non-GET/HEAD/OPTIONS request in the group against ASP.NET Core's built-in
/// double-submit-cookie antiforgery check, returning <c>400</c> instead of throwing on failure.
/// GET/HEAD/OPTIONS are exempt: they're not supposed to mutate anything, and requiring a token on
/// them would break plain-link/prefetch navigation for no CSRF benefit.
/// </summary>
public sealed class AntiforgeryEndpointFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;

        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context.HttpContext);
            }
            catch (AntiforgeryValidationException ex)
            {
                return Results.Problem(
                    title: "Invalid or missing antiforgery token.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        return await next(context);
    }
}
