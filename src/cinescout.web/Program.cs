using System.Security.Claims;
using cinescout.core.Discord;
using cinescout.core.HallOfFame;
using cinescout.core.Kinoheld;
using cinescout.core.WatchedMovies;
using cinescout.persistence;
using cinescout.web.Auth;
using cinescout.web.Client.Pages;
using cinescout.web.Components;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents()
    .AddAuthenticationStateSerialization();

builder.Services.AddCascadingAuthenticationState();

builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();

// The "Testing" environment (set by cinescout.web.Tests' WebApplicationFactory for tests that
// don't need persistence, e.g. the login gate) skips real Postgres/Hangfire wiring entirely —
// registering an unused DbContext is harmless, but starting Hangfire's server or migrating
// against a connection string that was never supplied is not.
var isTestingEnvironment = builder.Environment.IsEnvironment("Testing");
var connectionString = builder.Configuration.GetConnectionString("Postgres");

if (!isTestingEnvironment && connectionString is null)
{
    throw new InvalidOperationException("Connection string 'ConnectionStrings:Postgres' not found.");
}

builder.Services.AddDbContext<CineScoutDbContext>(options => options.UseNpgsql(connectionString ?? "Host=unused"));

if (!isTestingEnvironment)
{
    builder.Services.AddHangfire(config => config.UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
    builder.Services.AddHangfireServer();
}

builder.Services.AddHttpClient<IHallOfFameClient, HallOfFameClient>()
    .AddStandardResilienceHandler();
builder.Services.AddScoped<HallOfFameCrawlService>();
builder.Services.AddScoped<HallOfFameCrawlJob>();

builder.Services.AddHttpClient<IKinoheldClient, KinoheldClient>()
    .AddStandardResilienceHandler();
builder.Services.AddScoped<KinoheldRoomSeedingService>();

builder.Services.AddHttpClient<IDiscordNotifier, DiscordNotifier>()
    .AddStandardResilienceHandler();
builder.Services.AddScoped<WatchedMovieService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(builder.Configuration.GetValue("Auth:SessionLifetimeDays", 30));
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

if (!isTestingEnvironment)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CineScoutDbContext>().Database.MigrateAsync();
}

if (!isTestingEnvironment)
{
    // The static RecurringJob.AddOrUpdate facade needs the legacy global JobStorage.Current,
    // which the DI-based AddHangfire(...) registration above never sets — use the DI-resolved
    // IRecurringJobManager instead (Hangfire's own recommended fix, per its exception message).
    var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();
    var hallOfFameCrawlIntervalHours = app.Configuration.GetValue("HallOfFame:CrawlIntervalHours", 1);
    recurringJobManager.AddOrUpdate<HallOfFameCrawlJob>(
        "hall-of-fame-crawl",
        job => job.RunAsync(CancellationToken.None),
        $"0 */{hallOfFameCrawlIntervalHours} * * *");
}

// Room seeding is eager, not lazy: fetched once per Site from its Kinoheld widget config,
// independent of the regular seat crawl — not a recurring Hangfire job. Idempotent (upsert), so
// safe to re-run on every app restart, and self-healing if Kinoheld adds an auditorium later.
if (!isTestingEnvironment)
{
    await using var roomSeedingScope = app.Services.CreateAsyncScope();
    var roomSeedingDb = roomSeedingScope.ServiceProvider.GetRequiredService<CineScoutDbContext>();
    var roomSeeder = roomSeedingScope.ServiceProvider.GetRequiredService<KinoheldRoomSeedingService>();
    foreach (var site in await roomSeedingDb.Sites.Where(s => s.IsActive).ToListAsync())
    {
        await roomSeeder.SeedRoomsForSiteAsync(site, CancellationToken.None);
    }
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
