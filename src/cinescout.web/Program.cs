using System.Security.Claims;
using cinescout.core.Extensions;
using cinescout.persistence;
using cinescout.persistence.Extensions;
using cinescout.web.Auth;
using cinescout.web.Components;
using cinescout.web.Extensions;
using cinescout.web.HealthChecks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

// Docker's HEALTHCHECK invokes this in-process rather than depending on curl/wget being present
// in the runtime image — must short-circuit before CreateBuilder(args), so it never registers a
// second Hangfire server alongside the one already running for the real app process.
if (args.Contains("--healthcheck"))
{
    return await RunHealthCheckAsync();
}

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfiguration) =>
    loggerConfiguration.ReadFrom.Configuration(context.Configuration));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddCascadingAuthenticationState();

// The "Testing" environment (set by cinescout.web.Tests' WebApplicationFactory) skips Hangfire
// wiring and the startup migration, and doesn't require a connection string to be configured —
// but the DbContext registration itself still gets used by tests whose request path touches the
// database (e.g. the login gate now checking the seeded User row), so it can't be skipped outright.
var isTestingEnvironment = builder.Environment.IsEnvironment("Testing");
var connectionString = builder.Configuration.GetConnectionString("Postgres");

builder.Services.AddPersistence(builder.Configuration, requireConnectionString: !isTestingEnvironment);

var healthChecksBuilder = builder.Services.AddHealthChecks();

if (!isTestingEnvironment)
{
    builder.Services.AddHangfireInfrastructure(connectionString);
    healthChecksBuilder.AddCheck<HangfireHeartbeatHealthCheck>("hangfire");
}

builder.Services
    .AddHallOfFame()
    .AddKinoheld()
    .AddDiscord()
    .AddEmail()
    .AddMatching()
    .AddTrackedMovies()
    .AddPreferences();

builder.Services.AddCineScoutAuthentication(builder.Configuration);

// TLS terminates at the operator's own externally-managed Nginx Proxy Manager, not here (#46/#51)
// — trust its X-Forwarded-For/X-Forwarded-Proto headers so the app sees the real client IP/scheme.
// KnownIPNetworks comes from config, not hardcoded: the real subnet isn't knowable until the
// operator's external Docker network (shared with their NPM stack) actually exists.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    var knownNetwork = builder.Configuration["ForwardedHeaders:KnownNetwork"];
    if (!string.IsNullOrEmpty(knownNetwork))
    {
        options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(knownNetwork));
    }
});

var app = builder.Build();

if (!isTestingEnvironment)
{
    await app.Services.ApplyMigrationsAsync();
    await app.MigrateLegacyPasswordHashAsync();
    await app.LogFirstRunTokenIfNeededAsync();
    app.ScheduleRecurringJobs();
    await app.SeedKinoheldRoomsAsync();
}

// Must run before anything that inspects the request's scheme/remote IP (in particular
// UseAuthentication below, whose cookie handling cares about the real scheme) — otherwise it'd
// see NPM's internal HTTP hop instead of the real client's HTTPS/IP.
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();

app.MapHealthChecks("/healthz").AllowAnonymous();

app.MapPost("/account/login", async (HttpContext context, IPasswordHasher<AppUser> hasher, CineScoutDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync();
    if (user?.PasswordHash is null)
    {
        return Results.Redirect("/setup");
    }

    var submittedPassword = context.Request.Form["password"].ToString();
    var returnUrl = context.Request.Form["returnUrl"].ToString();

    var verified = hasher.VerifyHashedPassword(AppUser.Instance, user.PasswordHash, submittedPassword) != PasswordVerificationResult.Failed;

    if (!verified)
    {
        return Results.Redirect("/login?error=1");
    }

    await SignInOperatorAsync(context);

    return Results.Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
}).AllowAnonymous();

app.MapPost("/account/logout", async (HttpContext context) =>
{
    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    return Results.Redirect("/login");
});

app.MapPost("/account/setup", async (HttpContext context, IPasswordHasher<AppUser> hasher, CineScoutDbContext db, FirstRunTokenStore tokenStore) =>
{
    var user = await db.Users.FirstOrDefaultAsync();
    if (user is null || user.PasswordHash is not null)
    {
        return Results.Redirect("/login");
    }

    var submittedToken = context.Request.Form["token"].ToString();
    var password = context.Request.Form["password"].ToString();
    var confirmPassword = context.Request.Form["confirmPassword"].ToString();

    if (!tokenStore.Matches(submittedToken))
    {
        return Results.Redirect("/setup?error=token");
    }

    if (string.IsNullOrEmpty(password) || password != confirmPassword)
    {
        return Results.Redirect("/setup?error=password");
    }

    user.PasswordHash = hasher.HashPassword(AppUser.Instance, password);
    user.SetupCompletedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();

    await SignInOperatorAsync(context);

    return Results.Redirect("/");
}).AllowAnonymous();

static async Task<int> RunHealthCheckAsync()
{
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

    try
    {
        var response = await client.GetAsync("http://localhost:8080/healthz");
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        return 1;
    }
}

static async Task SignInOperatorAsync(HttpContext context)
{
    var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "cinescout")],
        CookieAuthenticationDefaults.AuthenticationScheme));

    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties { IsPersistent = true });
}

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(cinescout.web.Client._Imports).Assembly);

app.Run();
return 0;

public partial class Program;
