using cinescout.core.Kinoheld;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace cinescout.core.Tests;

public class KinoheldSeatCrawlServiceTests : IAsyncLifetime
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

    private static KinoheldSeatCrawlService CreateService(
        CineScoutDbContext db,
        IKinoheldClient client,
        KinoheldCircuitBreaker breaker,
        KinoheldFetchCooldownTracker cooldownTracker) =>
        new(
            db,
            client,
            breaker,
            cooldownTracker,
            new ConfigurationBuilder().Build(),
            NullLogger<KinoheldSeatCrawlService>.Instance);

    private static KinoheldSeatsResult.Success SuccessResult(params (string SeatId, string Row, int Number, string Status, string? Left, string? Right, string SectorId)[] seats) =>
        new(
            RawPayload: "{\"seats\":{}}", // must be valid JSON — SeatingSnapshot.RawPayload is a jsonb column
            Seats: seats
                .Select(s => new KinoheldSeat(s.SeatId, s.Row, s.Number, s.Status, s.Left, s.Right, s.SectorId))
                .ToList());

    private static async Task<int> SeedSiteAsync(CineScoutDbContext db, string? kinoheldCinemaId = "2135", string externalSiteId = "580")
    {
        var site = new Site
        {
            ExternalSiteId = externalSiteId,
            Name = "HALL OF FAME - Kino in Kamp-Lintfort",
            CrawlBaseUrl = "https://kamp-lintfort.hall-of-fame.website",
            IsActive = true,
            KinoheldCinemaId = kinoheldCinemaId,
        };
        db.Sites.Add(site);
        await db.SaveChangesAsync();
        return site.Id;
    }

    private static async Task<int> SeedFilmAsync(CineScoutDbContext db, int siteId, string externalFilmId, bool watched)
    {
        var film = new Film { SiteId = siteId, ExternalFilmId = externalFilmId, Title = $"Film {externalFilmId}" };
        db.Films.Add(film);
        await db.SaveChangesAsync();

        if (watched)
        {
            db.WatchedMovies.Add(new WatchedMovie { FilmId = film.Id, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        return film.Id;
    }

    private static async Task<int> SeedPerformanceAsync(
        CineScoutDbContext db,
        int siteId,
        int filmId,
        string sourcePerformanceId,
        DateTimeOffset startsAt,
        PerformanceStatus status = PerformanceStatus.Normal,
        int? roomId = null)
    {
        var performance = new Performance
        {
            FilmId = filmId,
            SiteId = siteId,
            RoomId = roomId,
            SourcePerformanceId = sourcePerformanceId,
            StartsAt = startsAt,
            BookingLink = $"https://www.kinoheld.de/kino-kamp-lintfort/hall-of-fame?mode=widget&change=no&showId={sourcePerformanceId}",
            Status = status,
            IsSoldOut = false,
            IsBookable = true,
            LastSeenAt = DateTimeOffset.UtcNow,
        };
        db.Performances.Add(performance);
        await db.SaveChangesAsync();
        return performance.Id;
    }

    private static async Task<int> SeedRoomAsync(CineScoutDbContext db, int siteId, string externalAuditoriumId, string name)
    {
        var room = new Room { SiteId = siteId, ExternalAuditoriumId = externalAuditoriumId, Name = name };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return room.Id;
    }

    private static DateTimeOffset Future => DateTimeOffset.UtcNow.AddDays(2);

    [Fact]
    public async Task Crawl_scopes_to_watched_future_normal_performances_only()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetSeatsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SuccessResult(("s1", "A", 1, "sf", null, null, "8259")));

        int watchedFuturePerformanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            var watchedFilmId = await SeedFilmAsync(db, siteId, "f-watched", watched: true);
            var unwatchedFilmId = await SeedFilmAsync(db, siteId, "f-unwatched", watched: false);

            watchedFuturePerformanceId = await SeedPerformanceAsync(db, siteId, watchedFilmId, "1001", Future);
            await SeedPerformanceAsync(db, siteId, watchedFilmId, "1002", DateTimeOffset.UtcNow.AddDays(-1)); // past
            await SeedPerformanceAsync(db, siteId, watchedFilmId, "1003", Future, PerformanceStatus.Cancelled); // cancelled
            await SeedPerformanceAsync(db, siteId, unwatchedFilmId, "1004", Future); // not watched

            var service = CreateService(db, client, new KinoheldCircuitBreaker(), new KinoheldFetchCooldownTracker());
            await service.CrawlWatchedAsync(CancellationToken.None);
        }

        await client.Received(1).GetSeatsAsync("2135", "1001", Arg.Any<CancellationToken>());
        Assert.Single(client.ReceivedCalls());

        await using var read = new CineScoutDbContext(options);
        var snapshots = await read.SeatingSnapshots.ToListAsync();
        Assert.Single(snapshots);
        Assert.Equal(watchedFuturePerformanceId, snapshots[0].PerformanceId);
    }

    [Fact]
    public async Task First_crawl_inserts_then_second_crawl_updates_removes_and_appends_history()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetSeatsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SuccessResult(
                ("s1", "D", 1, "sf", null, "s2", "8259"),
                ("s2", "D", 2, "ss", "s1", null, "8259"),
                ("s3", "E", 1, "sf", null, null, "8259")));

        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: true);
            performanceId = await SeedPerformanceAsync(db, siteId, filmId, "2001", Future);

            var service = CreateService(db, client, new KinoheldCircuitBreaker(), new KinoheldFetchCooldownTracker());
            await service.CrawlWatchedAsync(CancellationToken.None);
        }

        int s1StatusRowId;
        await using (var read = new CineScoutDbContext(options))
        {
            var statuses = await read.SeatStatuses.Where(s => s.PerformanceId == performanceId).OrderBy(s => s.SourceSeatId).ToListAsync();
            Assert.Equal(3, statuses.Count);
            Assert.Equal(["s1", "s2", "s3"], statuses.Select(s => s.SourceSeatId));
            Assert.Equal([SeatOccupancyStatus.Free, SeatOccupancyStatus.Sold, SeatOccupancyStatus.Free], statuses.Select(s => s.Status));
            Assert.Equal(["D", "D", "E"], statuses.Select(s => s.Row));
            Assert.Equal([1, 2, 1], statuses.Select(s => s.SeatNumber));
            Assert.Null(statuses[0].LeftNeighborSeatId);
            Assert.Equal("s2", statuses[0].RightNeighborSeatId);
            s1StatusRowId = statuses[0].Id;

            var snapshot = await read.SeatingSnapshots.SingleAsync(s => s.PerformanceId == performanceId);
            var snapshotSeats = await read.SeatingSnapshotSeats.Where(s => s.SnapshotId == snapshot.Id).ToListAsync();
            Assert.Equal(3, snapshotSeats.Count);
        }

        // Second crawl: s1 flips to sold, s3 vanished, s4 is new.
        client.GetSeatsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SuccessResult(
                ("s1", "D", 1, "ss", null, "s2", "8259"),
                ("s2", "D", 2, "ss", "s1", null, "8259"),
                ("s4", "E", 2, "sf", null, null, "8259")));

        await using (var db = new CineScoutDbContext(options))
        {
            var service = CreateService(db, client, new KinoheldCircuitBreaker(), new KinoheldFetchCooldownTracker());
            await service.CrawlWatchedAsync(CancellationToken.None);
        }

        await using (var finalRead = new CineScoutDbContext(options))
        {
            var statuses = await finalRead.SeatStatuses.Where(s => s.PerformanceId == performanceId).OrderBy(s => s.SourceSeatId).ToListAsync();
            Assert.Equal(["s1", "s2", "s4"], statuses.Select(s => s.SourceSeatId)); // s3 removed, s4 added
            Assert.Equal(SeatOccupancyStatus.Sold, statuses[0].Status); // s1 updated ...
            Assert.Equal(s1StatusRowId, statuses[0].Id); // ... in place, same row

            var snapshots = await finalRead.SeatingSnapshots.Where(s => s.PerformanceId == performanceId).OrderBy(s => s.Id).ToListAsync();
            Assert.Equal(2, snapshots.Count); // append-only history

            var firstSnapshotSeats = await finalRead.SeatingSnapshotSeats.Where(s => s.SnapshotId == snapshots[0].Id).OrderBy(s => s.SourceSeatId).ToListAsync();
            Assert.Equal(["s1", "s2", "s3"], firstSnapshotSeats.Select(s => s.SourceSeatId)); // crawl-1 history untouched
            Assert.Equal(SeatOccupancyStatus.Free, firstSnapshotSeats[0].Status);

            var secondSnapshotSeats = await finalRead.SeatingSnapshotSeats.Where(s => s.SnapshotId == snapshots[1].Id).OrderBy(s => s.SourceSeatId).ToListAsync();
            Assert.Equal(["s1", "s2", "s4"], secondSnapshotSeats.Select(s => s.SourceSeatId));
        }
    }

    [Fact]
    public async Task First_success_resolves_null_RoomId_via_seeded_room()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetSeatsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SuccessResult(("s1", "A", 1, "sf", null, null, "8259")));

        int performanceId;
        int expectedRoomId;
        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            await SeedRoomAsync(db, siteId, "8255", "Kino 1");
            expectedRoomId = await SeedRoomAsync(db, siteId, "8259", "Kino 3");
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: true);
            performanceId = await SeedPerformanceAsync(db, siteId, filmId, "3001", Future);

            var service = CreateService(db, client, new KinoheldCircuitBreaker(), new KinoheldFetchCooldownTracker());
            await service.CrawlWatchedAsync(CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var performance = await read.Performances.SingleAsync(p => p.Id == performanceId);
        Assert.Equal(expectedRoomId, performance.RoomId);
    }

    [Fact]
    public async Task Already_set_RoomId_stays_untouched()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetSeatsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SuccessResult(("s1", "A", 1, "sf", null, null, "8259")));

        int performanceId;
        int presetRoomId;
        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            presetRoomId = await SeedRoomAsync(db, siteId, "8255", "Kino 1");
            await SeedRoomAsync(db, siteId, "8259", "Kino 3"); // what the response's secId would resolve to
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: true);
            performanceId = await SeedPerformanceAsync(db, siteId, filmId, "3002", Future, roomId: presetRoomId);

            var service = CreateService(db, client, new KinoheldCircuitBreaker(), new KinoheldFetchCooldownTracker());
            await service.CrawlWatchedAsync(CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var performance = await read.Performances.SingleAsync(p => p.Id == performanceId);
        Assert.Equal(presetRoomId, performance.RoomId);
    }

    [Fact]
    public async Task No_matching_room_leaves_RoomId_null_without_crashing()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetSeatsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SuccessResult(("s1", "A", 1, "sf", null, null, "9999")));

        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            await SeedRoomAsync(db, siteId, "8259", "Kino 3");
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: true);
            performanceId = await SeedPerformanceAsync(db, siteId, filmId, "3003", Future);

            var service = CreateService(db, client, new KinoheldCircuitBreaker(), new KinoheldFetchCooldownTracker());
            await service.CrawlWatchedAsync(CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var performance = await read.Performances.SingleAsync(p => p.Id == performanceId);
        Assert.Null(performance.RoomId);
        Assert.Single(await read.SeatingSnapshots.Where(s => s.PerformanceId == performanceId).ToListAsync()); // still persisted normally
    }

    [Theory]
    [InlineData(true)] // HTTP 400: not currently bookable
    [InlineData(false)] // HTTP 404: show gone
    public async Task NotBookable_and_NotFound_are_skipped_without_writes_or_trip_and_crawl_continues(bool notBookable)
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        // Performance "4001" (earlier StartsAt) gets the expected error; "4002" succeeds.
        client.GetSeatsAsync(Arg.Any<string>(), "4001", Arg.Any<CancellationToken>())
            .Returns(notBookable ? new KinoheldSeatsResult.NotBookable() : new KinoheldSeatsResult.NotFound());
        client.GetSeatsAsync(Arg.Any<string>(), "4002", Arg.Any<CancellationToken>())
            .Returns(SuccessResult(("s1", "A", 1, "sf", null, null, "8259")));

        var breaker = new KinoheldCircuitBreaker();
        int erroredPerformanceId;
        int laterPerformanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: true);
            erroredPerformanceId = await SeedPerformanceAsync(db, siteId, filmId, "4001", Future);
            laterPerformanceId = await SeedPerformanceAsync(db, siteId, filmId, "4002", Future.AddHours(3));

            var service = CreateService(db, client, breaker, new KinoheldFetchCooldownTracker());
            await service.CrawlWatchedAsync(CancellationToken.None);
        }

        Assert.False(breaker.IsTripped);
        await client.Received(1).GetSeatsAsync("2135", "4002", Arg.Any<CancellationToken>()); // later performance still fetched

        await using var read = new CineScoutDbContext(options);
        Assert.Empty(await read.SeatingSnapshots.Where(s => s.PerformanceId == erroredPerformanceId).ToListAsync());
        Assert.Empty(await read.SeatStatuses.Where(s => s.PerformanceId == erroredPerformanceId).ToListAsync());
        Assert.Single(await read.SeatingSnapshots.Where(s => s.PerformanceId == laterPerformanceId).ToListAsync());
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public async Task Blocked_response_trips_breaker_stops_iteration_and_silences_next_crawl(int statusCode)
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetSeatsAsync(Arg.Any<string>(), "5001", Arg.Any<CancellationToken>())
            .Returns(new KinoheldSeatsResult.Blocked(statusCode));
        client.GetSeatsAsync(Arg.Any<string>(), "5002", Arg.Any<CancellationToken>())
            .Returns(SuccessResult(("s1", "A", 1, "sf", null, null, "8259")));

        var breaker = new KinoheldCircuitBreaker();
        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: true);
            await SeedPerformanceAsync(db, siteId, filmId, "5001", Future);
            await SeedPerformanceAsync(db, siteId, filmId, "5002", Future.AddHours(3));

            var service = CreateService(db, client, breaker, new KinoheldFetchCooldownTracker());
            await service.CrawlWatchedAsync(CancellationToken.None);
        }

        Assert.True(breaker.IsTripped);
        Assert.Contains(statusCode.ToString(), breaker.TripReason);
        await client.DidNotReceive().GetSeatsAsync(Arg.Any<string>(), "5002", Arg.Any<CancellationToken>()); // iteration stopped
        Assert.Single(client.ReceivedCalls());

        await using (var read = new CineScoutDbContext(options))
        {
            Assert.Empty(await read.SeatingSnapshots.ToListAsync()); // no writes at all
        }

        // A subsequent crawl with the tripped breaker makes zero client calls.
        client.ClearReceivedCalls();
        await using (var db = new CineScoutDbContext(options))
        {
            var service = CreateService(db, client, breaker, new KinoheldFetchCooldownTracker());
            await service.CrawlWatchedAsync(CancellationToken.None);
        }

        Assert.Empty(client.ReceivedCalls());
    }

    [Fact]
    public async Task Anomalous_response_trips_breaker_and_stops_iteration()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetSeatsAsync(Arg.Any<string>(), "5101", Arg.Any<CancellationToken>())
            .Returns(new KinoheldSeatsResult.Anomalous("200 response body did not parse as the expected seats shape."));
        client.GetSeatsAsync(Arg.Any<string>(), "5102", Arg.Any<CancellationToken>())
            .Returns(SuccessResult(("s1", "A", 1, "sf", null, null, "8259")));

        var breaker = new KinoheldCircuitBreaker();
        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: true);
            await SeedPerformanceAsync(db, siteId, filmId, "5101", Future);
            await SeedPerformanceAsync(db, siteId, filmId, "5102", Future.AddHours(3));

            var service = CreateService(db, client, breaker, new KinoheldFetchCooldownTracker());
            await service.CrawlWatchedAsync(CancellationToken.None);
        }

        Assert.True(breaker.IsTripped);
        await client.DidNotReceive().GetSeatsAsync(Arg.Any<string>(), "5102", Arg.Any<CancellationToken>());

        await using var read = new CineScoutDbContext(options);
        Assert.Empty(await read.SeatingSnapshots.ToListAsync());
    }

    [Fact]
    public async Task OnDemand_fresh_cache_serves_without_client_call()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();

        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: false);
            performanceId = await SeedPerformanceAsync(db, siteId, filmId, "6001", Future);

            db.SeatingSnapshots.Add(new SeatingSnapshot
            {
                PerformanceId = performanceId,
                CrawledAt = DateTimeOffset.UtcNow.AddMinutes(-1), // well within the 30-minute freshness window
                RawPayload = "{}",
            });
            await db.SaveChangesAsync();

            var service = CreateService(db, client, new KinoheldCircuitBreaker(), new KinoheldFetchCooldownTracker());
            var outcome = await service.FetchForPerformanceAsync(performanceId, forceRefresh: false, CancellationToken.None);

            Assert.Equal(SeatFetchOutcome.Fresh, outcome);
        }

        Assert.Empty(client.ReceivedCalls());
    }

    [Fact]
    public async Task OnDemand_stale_cache_fetches()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetSeatsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SuccessResult(("s1", "A", 1, "sf", null, null, "8259")));

        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: false);
            performanceId = await SeedPerformanceAsync(db, siteId, filmId, "6002", Future);

            db.SeatingSnapshots.Add(new SeatingSnapshot
            {
                PerformanceId = performanceId,
                CrawledAt = DateTimeOffset.UtcNow.AddHours(-2), // stale
                RawPayload = "{}",
            });
            await db.SaveChangesAsync();

            var service = CreateService(db, client, new KinoheldCircuitBreaker(), new KinoheldFetchCooldownTracker());
            var outcome = await service.FetchForPerformanceAsync(performanceId, forceRefresh: false, CancellationToken.None);

            Assert.Equal(SeatFetchOutcome.Fetched, outcome);
        }

        await client.Received(1).GetSeatsAsync("2135", "6002", Arg.Any<CancellationToken>());

        await using var read = new CineScoutDbContext(options);
        Assert.Equal(2, await read.SeatingSnapshots.CountAsync(s => s.PerformanceId == performanceId));
    }

    [Fact]
    public async Task Force_refresh_bypasses_freshness_but_second_force_within_cooldown_is_rejected()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetSeatsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SuccessResult(("s1", "A", 1, "sf", null, null, "8259")));

        int performanceId;
        var cooldownTracker = new KinoheldFetchCooldownTracker();
        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: false);
            performanceId = await SeedPerformanceAsync(db, siteId, filmId, "6003", Future);

            db.SeatingSnapshots.Add(new SeatingSnapshot
            {
                PerformanceId = performanceId,
                CrawledAt = DateTimeOffset.UtcNow, // perfectly fresh — force must bypass this
                RawPayload = "{}",
            });
            await db.SaveChangesAsync();

            var service = CreateService(db, client, new KinoheldCircuitBreaker(), cooldownTracker);

            var firstForce = await service.FetchForPerformanceAsync(performanceId, forceRefresh: true, CancellationToken.None);
            Assert.Equal(SeatFetchOutcome.Fetched, firstForce);

            var secondForce = await service.FetchForPerformanceAsync(performanceId, forceRefresh: true, CancellationToken.None);
            Assert.Equal(SeatFetchOutcome.CooldownActive, secondForce);
        }

        Assert.Single(client.ReceivedCalls()); // the cooldown rejection made no call
    }

    [Fact]
    public async Task Force_refresh_right_after_a_routine_fetch_is_not_cooled_down()
    {
        // The 30s cooldown exists to absorb accidental force-refresh double-clicks only (#14);
        // a routine stale-cache fetch on page view must not consume it and block a deliberate
        // force-refresh moments later.
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetSeatsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(SuccessResult(("s1", "A", 1, "sf", null, null, "8259")));

        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: false);
            var performanceId = await SeedPerformanceAsync(db, siteId, filmId, "6005", Future);

            db.SeatingSnapshots.Add(new SeatingSnapshot
            {
                PerformanceId = performanceId,
                CrawledAt = DateTimeOffset.UtcNow.AddHours(-2), // stale, so the routine fetch really goes out
                RawPayload = "{}",
            });
            await db.SaveChangesAsync();

            var service = CreateService(db, client, new KinoheldCircuitBreaker(), new KinoheldFetchCooldownTracker());

            var routine = await service.FetchForPerformanceAsync(performanceId, forceRefresh: false, CancellationToken.None);
            Assert.Equal(SeatFetchOutcome.Fetched, routine);

            var force = await service.FetchForPerformanceAsync(performanceId, forceRefresh: true, CancellationToken.None);
            Assert.Equal(SeatFetchOutcome.Fetched, force);
        }

        Assert.Equal(2, client.ReceivedCalls().Count());
    }

    [Fact]
    public async Task OnDemand_with_tripped_breaker_is_CircuitOpen_without_call()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        var breaker = new KinoheldCircuitBreaker();
        breaker.Trip("test trip");

        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db);
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: false);
            var performanceId = await SeedPerformanceAsync(db, siteId, filmId, "6004", Future);

            var service = CreateService(db, client, breaker, new KinoheldFetchCooldownTracker());
            var outcome = await service.FetchForPerformanceAsync(performanceId, forceRefresh: true, CancellationToken.None);

            Assert.Equal(SeatFetchOutcome.CircuitOpen, outcome);
        }

        Assert.Empty(client.ReceivedCalls());
    }

    [Fact]
    public async Task Unknown_performance_is_Unavailable()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();

        await using var db = new CineScoutDbContext(options);
        var service = CreateService(db, client, new KinoheldCircuitBreaker(), new KinoheldFetchCooldownTracker());

        var outcome = await service.FetchForPerformanceAsync(999999, forceRefresh: false, CancellationToken.None);

        Assert.Equal(SeatFetchOutcome.Unavailable, outcome);
        Assert.Empty(client.ReceivedCalls());
    }

    [Fact]
    public async Task Missing_KinoheldCinemaId_skips_without_call_or_crash()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        var breaker = new KinoheldCircuitBreaker();

        int performanceId;
        await using (var db = new CineScoutDbContext(options))
        {
            var siteId = await SeedSiteAsync(db, kinoheldCinemaId: null);
            var filmId = await SeedFilmAsync(db, siteId, "f1", watched: true);
            performanceId = await SeedPerformanceAsync(db, siteId, filmId, "7001", Future);

            var service = CreateService(db, client, breaker, new KinoheldFetchCooldownTracker());
            await service.CrawlWatchedAsync(CancellationToken.None);

            var onDemandOutcome = await service.FetchForPerformanceAsync(performanceId, forceRefresh: true, CancellationToken.None);
            Assert.Equal(SeatFetchOutcome.Unavailable, onDemandOutcome);
        }

        Assert.Empty(client.ReceivedCalls());
        Assert.False(breaker.IsTripped);

        await using var read = new CineScoutDbContext(options);
        Assert.Empty(await read.SeatingSnapshots.ToListAsync());
    }
}
