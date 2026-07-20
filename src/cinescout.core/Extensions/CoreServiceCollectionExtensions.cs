using System.Net;
using cinescout.core.Discord;
using cinescout.core.HallOfFame;
using cinescout.core.Kinoheld;
using cinescout.core.Preferences;
using cinescout.core.WatchedMovies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace cinescout.core.Extensions;

public static class CoreServiceCollectionExtensions
{
    public static IServiceCollection AddHallOfFame(this IServiceCollection services)
    {
        services.AddHttpClient<IHallOfFameClient, HallOfFameClient>()
            .AddStandardResilienceHandler();
        services.AddScoped<HallOfFameCrawlService>();
        services.AddScoped<HallOfFameCrawlJob>();

        return services;
    }

    public static IServiceCollection AddKinoheld(this IServiceCollection services)
    {
        // Deliberately honest crawling posture (#22): a non-spoofed User-Agent that names this tool, no
        // header spoofing. Standard resilience stays, but its retry strategy must NOT retry 403/429 —
        // retrying a block would worsen it; the KinoheldCircuitBreaker must see it and stop all polling.
        services.AddHttpClient<IKinoheldClient, KinoheldClient>(client =>
                // "(personal use)" with a space is not a valid UA comment token for ParseAdd — keep it hyphenated.
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CineScout/1.0 (personal-use)"))
            .AddStandardResilienceHandler(options =>
                options.Retry.ShouldHandle = args =>
                {
                    var statusCode = args.Outcome.Result?.StatusCode;
                    if (statusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                    {
                        return ValueTask.FromResult(false);
                    }

                    return ValueTask.FromResult(HttpClientResiliencePredicates.IsTransient(args.Outcome));
                });
        services.AddScoped<KinoheldRoomSeedingService>();
        services.AddSingleton<KinoheldCircuitBreaker>();
        services.AddSingleton<KinoheldFetchCooldownTracker>();
        services.AddScoped<KinoheldSeatCrawlService>();
        services.AddScoped<KinoheldSeatCrawlJob>();

        return services;
    }

    public static IServiceCollection AddDiscord(this IServiceCollection services)
    {
        services.AddHttpClient<IDiscordNotifier, DiscordNotifier>()
            .AddStandardResilienceHandler();

        return services;
    }

    public static IServiceCollection AddWatchedMovies(this IServiceCollection services)
    {
        services.AddScoped<WatchedMovieService>();

        return services;
    }

    public static IServiceCollection AddPreferences(this IServiceCollection services)
    {
        services.AddScoped<PreferenceService>();

        return services;
    }
}
