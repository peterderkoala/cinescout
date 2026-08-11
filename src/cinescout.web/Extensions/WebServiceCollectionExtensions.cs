using cinescout.core.Kinoheld;
using cinescout.core.HallOfFame;
using cinescout.model;
using cinescout.persistence;
using cinescout.web.Auth;
using cinescout.web.Client.Api;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Extensions;

public static class WebServiceCollectionExtensions
{
    /// <summary>
    /// Registers cinescout.web.Client's per-screen API client wrappers (and their
    /// AntiforgeryTokenStore/HttpClient dependencies) in the SERVER's DI container too — caught
    /// live by #92's smoke test, which 500'd with "no registered service of type
    /// CinemasApiClient". `@inject`-ed properties on an InteractiveWebAssembly page are resolved
    /// from whichever host is currently rendering it; the static-prerender pass that produces the
    /// initial HTML (issue #86: every WASM page gets one before the runtime boots) runs
    /// server-side, so it needs these types constructible even though the RendererInfo.IsInteractive
    /// gate means nothing on them is ever actually called until the client takes over. The
    /// server-side HttpClient has no BaseAddress and is never meant to make a real request — every
    /// future screen ticket that adds its own API client wrapper (#93–#98) must register it here
    /// too, or its page will 500 on first load the same way.
    /// </summary>
    public static IServiceCollection AddClientApiClientsForPrerendering(this IServiceCollection services)
    {
        services.AddScoped(_ => new HttpClient());
        services.AddScoped<AntiforgeryTokenStore>();
        services.AddScoped<CinemasApiClient>();
        services.AddScoped<TimePreferencesApiClient>();
        services.AddScoped<SeatMatricesApiClient>();
        services.AddScoped<HomeApiClient>();
        services.AddScoped<PerformanceDetailApiClient>();

        return services;
    }

    public static IServiceCollection AddCineScoutAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddSingleton<FirstRunTokenStore>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromDays(configuration.GetValue("Auth:SessionLifetimeDays", 30));

                // Same-origin app, no legitimate cross-site entry point — ADR 0004. Was an
                // unrecorded Lax default before.
                options.Cookie.SameSite = SameSiteMode.Strict;

                // Plain AddCookie()'s default Events unconditionally 302-redirect on challenge/
                // forbid, which a fetch() call follows transparently and lands on the /login HTML
                // with a 200 — ADR 0004's "opaque garbage" outcome. /api callers need a real status
                // code they can branch on instead.
                options.Events.OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    context.Response.Redirect(context.RedirectUri);
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }

    public static IServiceCollection AddHangfireInfrastructure(this IServiceCollection services, string? connectionString)
    {
        services.AddHangfire(config => config.UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString)));
        services.AddHangfireServer();

        return services;
    }

    public static async Task MigrateLegacyPasswordHashAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CineScoutDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        await LegacyPasswordHashMigrator.MigrateAsync(db, app.Configuration, logger);
    }

    public static async Task LogFirstRunTokenIfNeededAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CineScoutDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        var tokenStore = scope.ServiceProvider.GetRequiredService<FirstRunTokenStore>();

        var user = await db.Users.FirstOrDefaultAsync();
        if (user?.PasswordHash is null)
        {
            logger.LogInformation(
                "First-run setup required. Visit /setup and enter this token: {Token}", tokenStore.Token);
        }
    }

    public static void ScheduleRecurringJobs(this WebApplication app)
    {
        // The static RecurringJob.AddOrUpdate facade needs the legacy global JobStorage.Current,
        // which the DI-based AddHangfire(...) registration never sets — use the DI-resolved
        // IRecurringJobManager instead (Hangfire's own recommended fix, per its exception message).
        var recurringJobManager = app.Services.GetRequiredService<IRecurringJobManager>();

        var hallOfFameCrawlIntervalHours = app.Configuration.GetValue("HallOfFame:CrawlIntervalHours", 1);
        recurringJobManager.AddOrUpdate<HallOfFameCrawlJob>(
            "hall-of-fame-crawl",
            job => job.RunAsync(CancellationToken.None),
            $"0 */{hallOfFameCrawlIntervalHours} * * *");

        var kinoheldSeatCrawlIntervalMinutes = app.Configuration.GetValue("Kinoheld:SeatCrawlIntervalMinutes", 30);
        recurringJobManager.AddOrUpdate<KinoheldSeatCrawlJob>(
            "kinoheld-seat-crawl",
            job => job.RunAsync(CancellationToken.None),
            $"*/{kinoheldSeatCrawlIntervalMinutes} * * * *");
    }

    public static async Task SeedKinoheldRoomsAsync(this WebApplication app)
    {
        // Room seeding is eager, not lazy: fetched once per Cinema from its Kinoheld widget config,
        // independent of the regular seat crawl — not a recurring Hangfire job. Idempotent (upsert), so
        // safe to re-run on every app restart, and self-healing if Kinoheld adds an auditorium later.
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CineScoutDbContext>();
        var roomSeeder = scope.ServiceProvider.GetRequiredService<KinoheldRoomSeedingService>();

        foreach (var cinema in await db.Cinemas.Where(s => s.IsActive).ToListAsync())
        {
            await roomSeeder.SeedRoomsForCinemaAsync(cinema, CancellationToken.None);
        }
    }
}
