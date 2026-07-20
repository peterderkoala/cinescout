using System.Security.Claims;
using cinescout.core.Extensions;
using cinescout.persistence;
using cinescout.persistence.Extensions;
using cinescout.web.Auth;
using cinescout.web.Components;
using cinescout.web.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

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

if (!isTestingEnvironment)
{
    builder.Services.AddHangfireInfrastructure(connectionString);
}

builder.Services
    .AddHallOfFame()
    .AddKinoheld()
    .AddDiscord()
    .AddEmail()
    .AddMatching()
    .AddWatchedMovies()
    .AddPreferences();

builder.Services.AddCineScoutAuthentication(builder.Configuration);

var app = builder.Build();

if (!isTestingEnvironment)
{
    await app.Services.ApplyMigrationsAsync();
    await app.MigrateLegacyPasswordHashAsync();
    await app.LogFirstRunTokenIfNeededAsync();
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

public partial class Program;
