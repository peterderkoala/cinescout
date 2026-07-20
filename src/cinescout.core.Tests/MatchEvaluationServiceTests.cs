using cinescout.core.Discord;
using cinescout.core.Matching;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace cinescout.core.Tests;

public class MatchEvaluationServiceTests : IAsyncLifetime
{
    private const DaysOfWeekFlags AllDays = DaysOfWeekFlags.Monday | DaysOfWeekFlags.Tuesday | DaysOfWeekFlags.Wednesday
        | DaysOfWeekFlags.Thursday | DaysOfWeekFlags.Friday | DaysOfWeekFlags.Saturday | DaysOfWeekFlags.Sunday;

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

    private sealed record Scenario(int SiteId, int RoomId, int FilmId, int PerformanceId);

    private static async Task<Scenario> SeedScenarioAsync(CineScoutDbContext db, DateTimeOffset startsAt, string? posterUrl = null, bool watched = true)
    {
        var site = new Site
        {
            ExternalSiteId = "580",
            Name = "HALL OF FAME - Kino in Kamp-Lintfort",
            CrawlBaseUrl = "https://kamp-lintfort.hall-of-fame.website",
            IsActive = true,
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        var room = new Room { SiteId = site.Id, ExternalAuditoriumId = "8259", Name = "Kino 3" };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();

        var film = new Film { SiteId = site.Id, ExternalFilmId = "f1", Title = "Vaiana - Live Action", PosterUrl = posterUrl };
        db.Films.Add(film);
        await db.SaveChangesAsync();

        if (watched)
        {
            db.WatchedMovies.Add(new WatchedMovie { FilmId = film.Id, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var performance = new Performance
        {
            FilmId = film.Id,
            SiteId = site.Id,
            RoomId = room.Id,
            SourcePerformanceId = "74705",
            StartsAt = startsAt,
            BookingLink = "https://www.kinoheld.de/kino-kamp-lintfort/hall-of-fame?mode=widget&change=no&showId=74705",
            Status = PerformanceStatus.Normal,
            IsSoldOut = false,
            IsBookable = true,
            LastSeenAt = DateTimeOffset.UtcNow,
        };
        db.Performances.Add(performance);
        await db.SaveChangesAsync();

        return new Scenario(site.Id, room.Id, film.Id, performance.Id);
    }

    private static async Task ReplaceFreeAdjacentSeatsAsync(CineScoutDbContext db, int performanceId, string row, int count, string? priceAreaProviderId = null)
    {
        db.SeatStatuses.RemoveRange(db.SeatStatuses.Where(s => s.PerformanceId == performanceId));
        await db.SaveChangesAsync();

        for (var i = 1; i <= count; i++)
        {
            db.SeatStatuses.Add(new SeatStatus
            {
                PerformanceId = performanceId,
                SourceSeatId = $"{row}{i}",
                Row = row,
                SeatNumber = i,
                Status = SeatOccupancyStatus.Free,
                LeftNeighborSeatId = i > 1 ? $"{row}{i - 1}" : null,
                RightNeighborSeatId = i < count ? $"{row}{i + 1}" : null,
                PriceAreaProviderId = priceAreaProviderId,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

    private static async Task MarkSeatSoldAsync(CineScoutDbContext db, int performanceId, string sourceSeatId)
    {
        var seat = await db.SeatStatuses.SingleAsync(s => s.PerformanceId == performanceId && s.SourceSeatId == sourceSeatId);
        seat.Status = SeatOccupancyStatus.Sold;
        await db.SaveChangesAsync();
    }

    private static FavoriteTimeWindow AlwaysMatchingWindow() => new()
    {
        DaysOfWeek = AllDays,
        StartTime = TimeOnly.MinValue,
        EndTime = TimeOnly.MaxValue,
    };

    private static FavoriteSeatMatrix Matrix(int roomId, int? filmId, int partySize, string name = "General") => new()
    {
        RoomId = roomId,
        FilmId = filmId,
        Name = name,
        RowStart = "A",
        RowEnd = "Z",
        SeatNumberStart = 1,
        SeatNumberEnd = 99,
        PartySize = partySize,
        IsEnabled = true,
    };

    private static IDiscordNotifier SucceedingNotifier()
    {
        var notifier = Substitute.For<IDiscordNotifier>();
        notifier.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationResult(true, "204 No Content"));
        return notifier;
    }

    private static DateTimeOffset Future => DateTimeOffset.UtcNow.AddDays(2);

    [Fact]
    public async Task Evaluate_creates_an_Active_Match_and_fires_RulesMatched_when_a_performance_first_qualifies()
    {
        var options = BuildOptions();
        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db, Future);
            performanceId = scenario.PerformanceId;
            db.FavoriteTimeWindows.Add(AlwaysMatchingWindow());
            db.FavoriteSeatMatrices.Add(Matrix(scenario.RoomId, filmId: null, partySize: 2));
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 3);
        }

        var notifier = SucceedingNotifier();
        await using (var db = new CineScoutDbContext(options))
        {
            var service = new MatchEvaluationService(db, notifier, NullLogger<MatchEvaluationService>.Instance);
            await service.EvaluateAsync(performanceId, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var match = await read.Matches.SingleAsync(m => m.PerformanceId == performanceId);
        Assert.Equal(MatchStatus.Active, match.Status);
        Assert.True(match.HasSufficientSeats);

        var log = await read.NotificationLogs.SingleAsync();
        Assert.Equal(NotificationType.RulesMatched, log.NotificationType);
        Assert.Equal(match.Id, log.MatchId);
        Assert.Equal(NotificationStatus.Success, log.Status);

        await notifier.Received(1).SendAsync(
            Arg.Is<string>(m => m != null && m.Contains("Vaiana - Live Action")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evaluate_does_not_duplicate_the_Match_on_rerun_while_still_qualifying()
    {
        var options = BuildOptions();
        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db, Future);
            performanceId = scenario.PerformanceId;
            db.FavoriteTimeWindows.Add(AlwaysMatchingWindow());
            db.FavoriteSeatMatrices.Add(Matrix(scenario.RoomId, filmId: null, partySize: 2));
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 3);
        }

        var notifier = SucceedingNotifier();
        for (var i = 0; i < 2; i++)
        {
            await using var db = new CineScoutDbContext(options);
            var service = new MatchEvaluationService(db, notifier, NullLogger<MatchEvaluationService>.Instance);
            await service.EvaluateAsync(performanceId, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.Single(await read.Matches.Where(m => m.PerformanceId == performanceId).ToListAsync());
        await notifier.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Evaluate_fires_SeatAvailabilityChanged_only_when_sufficiency_flips()
    {
        var options = BuildOptions();
        int performanceId;
        int roomId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db, Future);
            performanceId = scenario.PerformanceId;
            roomId = scenario.RoomId;
            db.FavoriteTimeWindows.Add(AlwaysMatchingWindow());
            db.FavoriteSeatMatrices.Add(Matrix(roomId, filmId: null, partySize: 2));
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 2); // exactly enough
        }

        var notifier = SucceedingNotifier();

        // 1) Initial match creation (RulesMatched, 1 call).
        await using (var db = new CineScoutDbContext(options))
        {
            await new MatchEvaluationService(db, notifier, NullLogger<MatchEvaluationService>.Instance)
                .EvaluateAsync(performanceId, CancellationToken.None);
        }

        // 2) Seats drop below party size -> flips to insufficient -> SeatAvailabilityChanged (call #2).
        await using (var db = new CineScoutDbContext(options))
        {
            await MarkSeatSoldAsync(db, performanceId, "D1");
            await new MatchEvaluationService(db, notifier, NullLogger<MatchEvaluationService>.Instance)
                .EvaluateAsync(performanceId, CancellationToken.None);
        }

        // 3) Re-evaluate with no change -> still insufficient -> no new notification.
        await using (var db = new CineScoutDbContext(options))
        {
            await new MatchEvaluationService(db, notifier, NullLogger<MatchEvaluationService>.Instance)
                .EvaluateAsync(performanceId, CancellationToken.None);
        }

        await using (var read = new CineScoutDbContext(options))
        {
            var afterInsufficient = await read.Matches.SingleAsync(m => m.PerformanceId == performanceId);
            Assert.False(afterInsufficient.HasSufficientSeats);
        }

        await notifier.Received(2).SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        // 4) Seats become sufficient again -> flips back -> SeatAvailabilityChanged (call #3).
        await using (var db = new CineScoutDbContext(options))
        {
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 2);
            await new MatchEvaluationService(db, notifier, NullLogger<MatchEvaluationService>.Instance)
                .EvaluateAsync(performanceId, CancellationToken.None);
        }

        await using (var read = new CineScoutDbContext(options))
        {
            var afterRestored = await read.Matches.SingleAsync(m => m.PerformanceId == performanceId);
            Assert.True(afterRestored.HasSufficientSeats);

            var seatAvailabilityChangedLogs = await read.NotificationLogs
                .Where(n => n.NotificationType == NotificationType.SeatAvailabilityChanged)
                .ToListAsync();
            Assert.Equal(2, seatAvailabilityChangedLogs.Count);
        }

        await notifier.Received(3).SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Film_specific_matrix_takes_precedence_over_general_matrix()
    {
        var options = BuildOptions();
        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db, Future);
            performanceId = scenario.PerformanceId;
            db.FavoriteTimeWindows.Add(AlwaysMatchingWindow());
            // General matrix would be satisfied by 2 seats; film-specific override demands 5 (unmet).
            db.FavoriteSeatMatrices.Add(Matrix(scenario.RoomId, filmId: null, partySize: 2, "General"));
            db.FavoriteSeatMatrices.Add(Matrix(scenario.RoomId, filmId: scenario.FilmId, partySize: 5, "Film-specific"));
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 3);
        }

        var notifier = SucceedingNotifier();
        await using (var db = new CineScoutDbContext(options))
        {
            await new MatchEvaluationService(db, notifier, NullLogger<MatchEvaluationService>.Instance)
                .EvaluateAsync(performanceId, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.Empty(await read.Matches.Where(m => m.PerformanceId == performanceId).ToListAsync());
        await notifier.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_applicable_seat_matrix_never_matches()
    {
        var options = BuildOptions();
        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db, Future);
            performanceId = scenario.PerformanceId;
            db.FavoriteTimeWindows.Add(AlwaysMatchingWindow());
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 10);
        }

        var notifier = SucceedingNotifier();
        await using (var db = new CineScoutDbContext(options))
        {
            await new MatchEvaluationService(db, notifier, NullLogger<MatchEvaluationService>.Instance)
                .EvaluateAsync(performanceId, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.Empty(await read.Matches.Where(m => m.PerformanceId == performanceId).ToListAsync());
        await notifier.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Non_watched_film_is_a_no_op()
    {
        var options = BuildOptions();
        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db, Future, watched: false);
            performanceId = scenario.PerformanceId;
            db.FavoriteTimeWindows.Add(AlwaysMatchingWindow());
            db.FavoriteSeatMatrices.Add(Matrix(scenario.RoomId, filmId: null, partySize: 2));
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 3);
        }

        var notifier = SucceedingNotifier();
        await using (var db = new CineScoutDbContext(options))
        {
            await new MatchEvaluationService(db, notifier, NullLogger<MatchEvaluationService>.Instance)
                .EvaluateAsync(performanceId, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.Empty(await read.Matches.ToListAsync());
        await notifier.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unresolved_RoomId_is_a_no_op()
    {
        var options = BuildOptions();
        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db, Future);
            performanceId = scenario.PerformanceId;
            var performance = await db.Performances.SingleAsync(p => p.Id == performanceId);
            performance.RoomId = null;
            db.FavoriteTimeWindows.Add(AlwaysMatchingWindow());
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 3);
        }

        var notifier = SucceedingNotifier();
        await using (var db = new CineScoutDbContext(options))
        {
            await new MatchEvaluationService(db, notifier, NullLogger<MatchEvaluationService>.Instance)
                .EvaluateAsync(performanceId, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.Empty(await read.Matches.ToListAsync());
        await notifier.DidNotReceive().SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RulesMatched_message_includes_the_cheapest_matching_price_and_the_poster_url()
    {
        var options = BuildOptions();
        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db, Future, posterUrl: "https://cdn.example/poster.jpg");
            performanceId = scenario.PerformanceId;
            db.FavoriteTimeWindows.Add(AlwaysMatchingWindow());
            db.FavoriteSeatMatrices.Add(Matrix(scenario.RoomId, filmId: null, partySize: 2));
            db.PerformancePriceAreas.AddRange(
                new PerformancePriceArea { PerformanceId = performanceId, ProviderId = "1", Name = "Komfort", OrderPrice = 15.36m },
                new PerformancePriceArea { PerformanceId = performanceId, ProviderId = "2", Name = "Premium", OrderPrice = 19.90m });
            await db.SaveChangesAsync();
            // Seats belong to the cheaper "1" price area — the notification should report 15.36, not 19.90.
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 3, priceAreaProviderId: "1");
        }

        var notifier = SucceedingNotifier();
        await using (var db = new CineScoutDbContext(options))
        {
            await new MatchEvaluationService(db, notifier, NullLogger<MatchEvaluationService>.Instance)
                .EvaluateAsync(performanceId, CancellationToken.None);
        }

        await notifier.Received(1).SendAsync(
            Arg.Is<string>(m => m != null && m.Contains("15.36") && !m.Contains("19.90") && m.Contains("https://cdn.example/poster.jpg")),
            Arg.Any<CancellationToken>());
    }
}
