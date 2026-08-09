using cinescout.core.Discord;
using cinescout.core.TrackedMovies;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace cinescout.core.Tests;

public class TrackedMovieServiceTests : IAsyncLifetime
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

    private async Task<(int CinemaId, int FilmId)> SeedFilmAsync()
    {
        var opts = BuildOptions();
        await using var setup = new CineScoutDbContext(opts);

        var cinema = new Cinema
        {
            ExternalCinemaId = "580",
            Name = "HALL OF FAME - Kino in Kamp-Lintfort",
            CrawlBaseUrl = "https://kamp-lintfort.hall-of-fame.website/programm/api/filtered-films",
            IsActive = true,
        };
        setup.Cinemas.Add(cinema);
        await setup.SaveChangesAsync();

        var film = new Film { CinemaId = cinema.Id, ExternalFilmId = "401865", Title = "Vaiana - Live Action" };
        setup.Films.Add(film);
        await setup.SaveChangesAsync();

        return (cinema.Id, film.Id);
    }

    [Fact]
    public async Task Track_creates_TrackedMovie_row_and_logs_TrackStarted()
    {
        var options = BuildOptions();
        var (_, filmId) = await SeedFilmAsync();

        var notifier = Substitute.For<IDiscordNotifier>();
        notifier.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationResult(true, "204 No Content"));

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new TrackedMovieService(db, notifier);
            await service.TrackAsync(filmId, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var trackedMovie = await read.TrackedMovies.SingleAsync(w => w.FilmId == filmId);
        Assert.True((DateTimeOffset.UtcNow - trackedMovie.CreatedAt) < TimeSpan.FromMinutes(1));

        var log = await read.NotificationLogs.SingleAsync();
        Assert.Equal(NotificationType.TrackStarted, log.NotificationType);
        Assert.Null(log.MatchId);
        Assert.Equal("Discord", log.Channel);
        Assert.Equal(NotificationStatus.Success, log.Status);

        await notifier.Received(1).SendAsync(
            Arg.Is<string>(m => m != null && m.Contains("Vaiana - Live Action")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Untrack_removes_TrackedMovie_row_and_logs_TrackStopped()
    {
        var options = BuildOptions();
        var (_, filmId) = await SeedFilmAsync();

        var notifier = Substitute.For<IDiscordNotifier>();
        notifier.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationResult(true, "204 No Content"));

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new TrackedMovieService(db, notifier);
            await service.TrackAsync(filmId, CancellationToken.None);
        }

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new TrackedMovieService(db, notifier);
            await service.UntrackAsync(filmId, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.False(await read.TrackedMovies.AnyAsync(w => w.FilmId == filmId));

        var logs = await read.NotificationLogs.OrderBy(l => l.SentAt).ToListAsync();
        Assert.Equal(2, logs.Count);
        Assert.Equal(NotificationType.TrackStarted, logs[0].NotificationType);
        Assert.Equal(NotificationType.TrackStopped, logs[1].NotificationType);

        await notifier.Received(1).SendAsync(
            Arg.Is<string>(m => m != null && m.Contains("Stopped tracking") && m.Contains("Vaiana - Live Action")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Notifier_failure_still_logs_the_attempt_without_rolling_back_the_track()
    {
        var options = BuildOptions();
        var (_, filmId) = await SeedFilmAsync();

        var notifier = Substitute.For<IDiscordNotifier>();
        notifier.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationResult(false, "Discord webhook URL not configured."));

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new TrackedMovieService(db, notifier);
            await service.TrackAsync(filmId, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.True(await read.TrackedMovies.AnyAsync(w => w.FilmId == filmId));

        var log = await read.NotificationLogs.SingleAsync();
        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Equal("Discord webhook URL not configured.", log.ResponseDetail);
    }

    [Fact]
    public async Task Tracking_an_already_tracked_film_is_a_safe_no_op()
    {
        var options = BuildOptions();
        var (_, filmId) = await SeedFilmAsync();

        var notifier = Substitute.For<IDiscordNotifier>();
        notifier.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationResult(true, "204 No Content"));

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new TrackedMovieService(db, notifier);
            await service.TrackAsync(filmId, CancellationToken.None);
            await service.TrackAsync(filmId, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.Equal(1, await read.TrackedMovies.CountAsync(w => w.FilmId == filmId));
        Assert.Equal(1, await read.NotificationLogs.CountAsync());

        await notifier.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Untracking_a_film_that_is_not_tracked_is_a_safe_no_op()
    {
        var options = BuildOptions();
        var (_, filmId) = await SeedFilmAsync();

        var notifier = Substitute.For<IDiscordNotifier>();

        await using var db = new CineScoutDbContext(options);
        var service = new TrackedMovieService(db, notifier);
        await service.UntrackAsync(filmId, CancellationToken.None);

        Assert.False(await db.TrackedMovies.AnyAsync(w => w.FilmId == filmId));
        Assert.Equal(0, await db.NotificationLogs.CountAsync());
        await notifier.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
