using System.Security.Claims;
using cinescout.core.Extensions;
using cinescout.persistence.Extensions;
using cinescout.web.Auth;
using cinescout.web.Client.Pages;
using cinescout.web.Components;
using cinescout.web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddCascadingAuthenticationState();

// The "Testing" environment (set by cinescout.web.Tests' WebApplicationFactory for tests that
// don't need persistence, e.g. the login gate) skips real Postgres/Hangfire wiring entirely —
// registering an unused DbContext is harmless, but starting Hangfire's server or migrating
// against a connection string that was never supplied is not.
var isTestingEnvironment = builder.Environment.IsEnvironment("Testing");
var connectionString = builder.Configuration.GetConnectionString("Postgres");

builder.Services.AddPersistence(builder.Configuration, requireConnectionString: !isTestingEnvironment);

if (!isTestingEnvironment)
{
    builder.Services.AddHangfireInfrastructure(connectionString);
}

builder.Services
    .AddHallOfFame()
    .AddKinoheld()
    .AddDiscord()
    .AddWatchedMovies()
    .AddPreferences();

builder.Services.AddCineScoutAuthentication(builder.Configuration);

var app = builder.Build();

if (!isTestingEnvironment)
{
    await app.Services.ApplyMigrationsAsync();
    app.ScheduleRecurringJobs();
    await app.SeedKinoheldRoomsAsync();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();

app.MapPost("/account/login", async (HttpContext context, IPasswordHasher<AppUser> hasher, IConfiguration config) =>
{
    var configuredHash = config["Auth:PasswordHash"];
    var submittedPassword = context.Request.Form["password"].ToString();
    var returnUrl = context.Request.Form["returnUrl"].ToString();

    var verified = !string.IsNullOrEmpty(configuredHash)
        && hasher.VerifyHashedPassword(AppUser.Instance, configuredHash, submittedPassword) != PasswordVerificationResult.Failed;

    if (!verified)
    {
        return Results.Redirect("/login?error=1");
    }

    var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "cinescout")],
        CookieAuthenticationDefaults.AuthenticationScheme));

    await context.SignInAsync(
        CookieAuthenticationDefaults.AuthenticationScheme,
        principal,
        new AuthenticationProperties { IsPersistent = true });

    return Results.Redirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
}).AllowAnonymous();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(cinescout.web.Client._Imports).Assembly);

app.Run();

public partial class Program;
