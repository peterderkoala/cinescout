using cinescout.core.Discord;
using cinescout.core.HallOfFame;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace cinescout.core.Tests;

public class HallOfFameCrawlJobTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = BuildOptions();
        await using var context = new CineScoutDbContext(options);
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }

    private DbContextOptions<CineScoutDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<CineScoutDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

    private static IDiscordNotifier CreateNotifier()
    {
        var notifier = Substitute.For<IDiscordNotifier>();
        notifier.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationResult(true, "204 No Content"));
        return notifier;
    }

    [Fact]
    public async Task One_cinema_throwing_does_not_skip_the_rest_of_the_cycle()
    {
        var options = BuildOptions();

        int failingCinemaId;
        int survivingCinemaId;
        await using (var setup = new CineScoutDbContext(options))
        {
            // Sorts before the surviving cinema by Id (insertion order) — reproduces the
            // pre-fix bug where an earlier failure aborted the whole foreach.
            var failing = new Cinema
            {
                ExternalCinemaId = "fail-1",
                Name = "Failing Cinema",
                CrawlBaseUrl = "https://failing.invalid",
                IsActive = true,
            };
            var surviving = new Cinema
            {
                ExternalCinemaId = "ok-1",
                Name = "Surviving Cinema",
                CrawlBaseUrl = "https://ok.invalid",
                IsActive = true,
            };
            setup.Cinemas.AddRange(failing, surviving);
            await setup.SaveChangesAsync();
            failingCinemaId = failing.Id;
            survivingCinemaId = surviving.Id;
        }

        var client = Substitute.For<IHallOfFameClient>();
        client.GetScheduleAsync("https://failing.invalid", Arg.Any<CancellationToken>())
            .Returns<HallOfFameScheduleResponse>(_ => throw new HttpRequestException("simulated failure"));
        client.GetScheduleAsync("https://ok.invalid", Arg.Any<CancellationToken>())
            .Returns(new HallOfFameScheduleResponse { Films = [] });

        await using (var db = new CineScoutDbContext(options))
        {
            var crawlService = new HallOfFameCrawlService(db, client, CreateNotifier());
            var job = new HallOfFameCrawlJob(db, crawlService, new ConfigurationBuilder().Build(), NullLogger<HallOfFameCrawlJob>.Instance);

            // Must not throw — a failing cinema is logged and skipped, not left to abort the job.
            await job.RunAsync(CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var failingCinema = await read.Cinemas.SingleAsync(c => c.Id == failingCinemaId);
        var survivingCinema = await read.Cinemas.SingleAsync(c => c.Id == survivingCinemaId);

        Assert.Null(failingCinema.LastCrawlAt);
        Assert.NotNull(survivingCinema.LastCrawlAt);
    }
}
