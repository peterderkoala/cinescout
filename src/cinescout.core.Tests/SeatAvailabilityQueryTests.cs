using cinescout.core.Matching;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace cinescout.core.Tests;

public class SeatAvailabilityQueryTests : IAsyncLifetime
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

    private sealed record Scenario(int RoomId, int FilmId, int PerformanceId);

    private static async Task<Scenario> SeedScenarioAsync(CineScoutDbContext db, bool watched = false, int? roomId = null)
    {
        var site = new Site
        {
            ExternalSiteId = Guid.NewGuid().ToString(),
            Name = "HALL OF FAME - Kino in Kamp-Lintfort",
            CrawlBaseUrl = "https://kamp-lintfort.hall-of-fame.website",
            IsActive = true,
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        int resolvedRoomId;
        if (roomId is int existingRoomId)
        {
            resolvedRoomId = existingRoomId;
        }
        else
        {
            var room = new Room { SiteId = site.Id, ExternalAuditoriumId = "8259", Name = "Kino 3" };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
            resolvedRoomId = room.Id;
        }

        var film = new Film { SiteId = site.Id, ExternalFilmId = "f1", Title = "Vaiana - Live Action" };
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
            RoomId = resolvedRoomId,
            SourcePerformanceId = "74705",
            StartsAt = DateTimeOffset.UtcNow.AddDays(2),
            BookingLink = "https://www.kinoheld.de/kino-kamp-lintfort/hall-of-fame?mode=widget&change=no&showId=74705",
            Status = PerformanceStatus.Normal,
            IsSoldOut = false,
            IsBookable = true,
            LastSeenAt = DateTimeOffset.UtcNow,
        };
        db.Performances.Add(performance);
        await db.SaveChangesAsync();

        return new Scenario(resolvedRoomId, film.Id, performance.Id);
    }

    private static async Task ReplaceFreeAdjacentSeatsAsync(CineScoutDbContext db, int performanceId, string row, int count)
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
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }

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

    [Fact]
    public async Task Returns_true_when_an_applicable_enabled_matrix_has_enough_contiguous_free_seats()
    {
        var options = BuildOptions();
        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db);
            performanceId = scenario.PerformanceId;
            db.FavoriteSeatMatrices.Add(Matrix(scenario.RoomId, filmId: null, partySize: 2));
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 3);
        }

        await using var db2 = new CineScoutDbContext(options);
        var result = await new SeatAvailabilityQuery(db2).HasOpenFavoriteMatrixSeatsAsync(performanceId, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task Returns_true_for_a_non_watched_film_the_same_as_a_watched_one()
    {
        var options = BuildOptions();
        int watchedPerformanceId;
        int nonWatchedPerformanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var watchedScenario = await SeedScenarioAsync(db, watched: true);
            watchedPerformanceId = watchedScenario.PerformanceId;
            db.FavoriteSeatMatrices.Add(Matrix(watchedScenario.RoomId, filmId: null, partySize: 2));
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, watchedPerformanceId, "D", count: 3);

            // Separate room so its general matrix doesn't also apply to the watched performance above.
            var nonWatchedScenario = await SeedScenarioAsync(db, watched: false);
            nonWatchedPerformanceId = nonWatchedScenario.PerformanceId;
            db.FavoriteSeatMatrices.Add(Matrix(nonWatchedScenario.RoomId, filmId: null, partySize: 2));
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, nonWatchedPerformanceId, "D", count: 3);
        }

        await using var db2 = new CineScoutDbContext(options);
        var query = new SeatAvailabilityQuery(db2);
        var watchedResult = await query.HasOpenFavoriteMatrixSeatsAsync(watchedPerformanceId, CancellationToken.None);
        var nonWatchedResult = await query.HasOpenFavoriteMatrixSeatsAsync(nonWatchedPerformanceId, CancellationToken.None);

        Assert.True(watchedResult);
        Assert.True(nonWatchedResult);
    }

    [Fact]
    public async Task Returns_false_when_no_applicable_seat_matrix_exists()
    {
        var options = BuildOptions();
        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db);
            performanceId = scenario.PerformanceId;
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 10);
        }

        await using var db2 = new CineScoutDbContext(options);
        var result = await new SeatAvailabilityQuery(db2).HasOpenFavoriteMatrixSeatsAsync(performanceId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Returns_false_when_the_applicable_matrix_has_no_contiguous_free_block_large_enough()
    {
        var options = BuildOptions();
        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db);
            performanceId = scenario.PerformanceId;
            db.FavoriteSeatMatrices.Add(Matrix(scenario.RoomId, filmId: null, partySize: 4));
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 3);
        }

        await using var db2 = new CineScoutDbContext(options);
        var result = await new SeatAvailabilityQuery(db2).HasOpenFavoriteMatrixSeatsAsync(performanceId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Film_specific_matrix_takes_precedence_over_general_matrix()
    {
        var options = BuildOptions();
        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db);
            performanceId = scenario.PerformanceId;
            // General matrix would be satisfied by 2 seats; film-specific override demands 5 (unmet).
            db.FavoriteSeatMatrices.Add(Matrix(scenario.RoomId, filmId: null, partySize: 2, "General"));
            db.FavoriteSeatMatrices.Add(Matrix(scenario.RoomId, filmId: scenario.FilmId, partySize: 5, "Film-specific"));
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 3);
        }

        await using var db2 = new CineScoutDbContext(options);
        var result = await new SeatAvailabilityQuery(db2).HasOpenFavoriteMatrixSeatsAsync(performanceId, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task Returns_false_for_a_performance_with_no_resolved_room()
    {
        var options = BuildOptions();
        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var scenario = await SeedScenarioAsync(db);
            performanceId = scenario.PerformanceId;
            var performance = await db.Performances.SingleAsync(p => p.Id == performanceId);
            performance.RoomId = null;
            db.FavoriteSeatMatrices.Add(Matrix(scenario.RoomId, filmId: null, partySize: 2));
            await db.SaveChangesAsync();
            await ReplaceFreeAdjacentSeatsAsync(db, performanceId, "D", count: 3);
        }

        await using var db2 = new CineScoutDbContext(options);
        var result = await new SeatAvailabilityQuery(db2).HasOpenFavoriteMatrixSeatsAsync(performanceId, CancellationToken.None);

        Assert.False(result);
    }
}
