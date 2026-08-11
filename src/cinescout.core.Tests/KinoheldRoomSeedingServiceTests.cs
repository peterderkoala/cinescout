using cinescout.core.Kinoheld;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace cinescout.core.Tests;

public class KinoheldRoomSeedingServiceTests : IAsyncLifetime
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

    private static KinoheldRoomSeedingService CreateService(
        CineScoutDbContext db,
        IKinoheldClient client,
        KinoheldCircuitBreaker? breaker = null,
        KinoheldRoomSeedCooldownTracker? cooldownTracker = null) =>
        new(
            db,
            client,
            breaker ?? new KinoheldCircuitBreaker(),
            cooldownTracker ?? new KinoheldRoomSeedCooldownTracker(),
            NullLogger<KinoheldRoomSeedingService>.Instance);

    private static KinoheldWidgetConfig ThreeAuditoriumConfig() => new()
    {
        CinemaId = "2135",
        Auditoriums =
        [
            new KinoheldAuditorium { Id = "8255", Name = "Kino 1" },
            new KinoheldAuditorium { Id = "8257", Name = "Kino 2" },
            new KinoheldAuditorium { Id = "8259", Name = "Kino 3" },
        ],
    };

    private async Task<(int CinemaId, string BookingLink)> SeedCinemaWithPerformanceAsync(CineScoutDbContext db)
    {
        var cinema = new Cinema
        {
            ExternalCinemaId = "580",
            Name = "HALL OF FAME - Kino in Kamp-Lintfort",
            CrawlBaseUrl = "https://kamp-lintfort.hall-of-fame.website",
            IsActive = true,
        };
        db.Cinemas.Add(cinema);
        await db.SaveChangesAsync();

        var film = new Film
        {
            CinemaId = cinema.Id,
            ExternalFilmId = "401865",
            Title = "Vaiana - Live Action",
        };
        db.Films.Add(film);
        await db.SaveChangesAsync();

        const string bookingLink = "https://www.kinoheld.de/kino-kamp-lintfort/hall-of-fame?mode=widget&change=no&showId=1";

        db.Performances.Add(new Performance
        {
            FilmId = film.Id,
            CinemaId = cinema.Id,
            SourcePerformanceId = "1",
            StartsAt = new DateTimeOffset(2026, 7, 18, 19, 0, 0, TimeSpan.Zero),
            BookingLink = bookingLink,
            Status = PerformanceStatus.Normal,
            IsSoldOut = false,
            IsBookable = true,
            LastSeenAt = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero),
        });
        await db.SaveChangesAsync();

        return (cinema.Id, bookingLink);
    }

    [Fact]
    public async Task Seeds_rooms_from_widget_config_when_none_exist()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetWidgetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new KinoheldWidgetConfigResult.Success(ThreeAuditoriumConfig()));

        int cinemaId;
        RoomSeedOutcome outcome;
        await using (var db = new CineScoutDbContext(options))
        {
            (cinemaId, _) = await SeedCinemaWithPerformanceAsync(db);

            var service = CreateService(db, client);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);

            outcome = await service.SeedRoomsForCinemaAsync(cinema, CancellationToken.None);
        }

        Assert.Equal(RoomSeedOutcome.Seeded, outcome);

        await using var read = new CineScoutDbContext(options);
        var rooms = await read.Rooms.Where(r => r.CinemaId == cinemaId).OrderBy(r => r.ExternalAuditoriumId).ToListAsync();

        Assert.Equal(3, rooms.Count);
        Assert.Equal(["8255", "8257", "8259"], rooms.Select(r => r.ExternalAuditoriumId));
        Assert.Equal(["Kino 1", "Kino 2", "Kino 3"], rooms.Select(r => r.Name));

        // The widget config's cinema id is captured onto the Cinema in the same save — the seat
        // crawl (#22) needs it as "cid" and never guesses it.
        var persistedCinema = await read.Cinemas.SingleAsync(s => s.Id == cinemaId);
        Assert.Equal("2135", persistedCinema.KinoheldCinemaId);
    }

    [Fact]
    public async Task Re_running_with_same_config_is_idempotent()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetWidgetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new KinoheldWidgetConfigResult.Success(ThreeAuditoriumConfig()));

        int cinemaId;
        await using (var db = new CineScoutDbContext(options))
        {
            (cinemaId, _) = await SeedCinemaWithPerformanceAsync(db);
        }

        // Each iteration gets its own cooldown tracker — this test is about seeding idempotency,
        // not the cooldown, which has its own tests below.
        for (var i = 0; i < 2; i++)
        {
            await using var db = new CineScoutDbContext(options);
            var service = CreateService(db, client);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.SeedRoomsForCinemaAsync(cinema, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var rooms = await read.Rooms.Where(r => r.CinemaId == cinemaId).ToListAsync();

        Assert.Equal(3, rooms.Count);
    }

    [Fact]
    public async Task Re_running_with_changed_name_updates_existing_room_in_place()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetWidgetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new KinoheldWidgetConfigResult.Success(ThreeAuditoriumConfig()));

        int cinemaId;
        await using (var db = new CineScoutDbContext(options))
        {
            (cinemaId, _) = await SeedCinemaWithPerformanceAsync(db);
            var service = CreateService(db, client);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.SeedRoomsForCinemaAsync(cinema, CancellationToken.None);
        }

        int originalRoomId;
        await using (var read = new CineScoutDbContext(options))
        {
            originalRoomId = (await read.Rooms.SingleAsync(r => r.CinemaId == cinemaId && r.ExternalAuditoriumId == "8259")).Id;
        }

        client.GetWidgetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new KinoheldWidgetConfigResult.Success(new KinoheldWidgetConfig
            {
                CinemaId = "2135",
                Auditoriums =
                [
                    new KinoheldAuditorium { Id = "8255", Name = "Kino 1" },
                    new KinoheldAuditorium { Id = "8257", Name = "Kino 2" },
                    new KinoheldAuditorium { Id = "8259", Name = "Kino 3 (Renamed)" },
                ],
            }));

        await using (var db = new CineScoutDbContext(options))
        {
            var service = CreateService(db, client);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.SeedRoomsForCinemaAsync(cinema, CancellationToken.None);
        }

        await using var finalRead = new CineScoutDbContext(options);
        var rooms = await finalRead.Rooms.Where(r => r.CinemaId == cinemaId).ToListAsync();

        Assert.Equal(3, rooms.Count);
        var renamedRoom = await finalRead.Rooms.SingleAsync(r => r.Id == originalRoomId);
        Assert.Equal("8259", renamedRoom.ExternalAuditoriumId);
        Assert.Equal("Kino 3 (Renamed)", renamedRoom.Name);
    }

    [Fact]
    public async Task No_bookingLink_yet_is_a_safe_no_op()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetWidgetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new KinoheldWidgetConfigResult.Success(ThreeAuditoriumConfig()));

        int cinemaId;
        RoomSeedOutcome outcome;
        await using (var db = new CineScoutDbContext(options))
        {
            var cinema = new Cinema
            {
                ExternalCinemaId = "581",
                Name = "Cinema with no crawled performances yet",
                CrawlBaseUrl = "https://example.invalid",
                IsActive = true,
            };
            db.Cinemas.Add(cinema);
            await db.SaveChangesAsync();
            cinemaId = cinema.Id;

            var service = CreateService(db, client);
            outcome = await service.SeedRoomsForCinemaAsync(cinema, CancellationToken.None);
        }

        Assert.Equal(RoomSeedOutcome.Unavailable, outcome);
        await client.DidNotReceive().GetWidgetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        await using var read = new CineScoutDbContext(options);
        var rooms = await read.Rooms.Where(r => r.CinemaId == cinemaId).ToListAsync();
        Assert.Empty(rooms);
    }

    [Fact]
    public async Task Pre_tripped_breaker_short_circuits_without_calling_out()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        var breaker = new KinoheldCircuitBreaker();
        breaker.Trip("pre-tripped for this test");

        await using var db = new CineScoutDbContext(options);
        var (cinemaId, _) = await SeedCinemaWithPerformanceAsync(db);
        var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);

        var service = CreateService(db, client, breaker: breaker);
        var outcome = await service.SeedRoomsForCinemaAsync(cinema, CancellationToken.None);

        Assert.Equal(RoomSeedOutcome.CircuitOpen, outcome);
        await client.DidNotReceive().GetWidgetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Blocked_widget_config_response_trips_breaker_and_returns_CircuitOpen()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetWidgetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new KinoheldWidgetConfigResult.Blocked(403));
        var breaker = new KinoheldCircuitBreaker();

        await using var db = new CineScoutDbContext(options);
        var (cinemaId, _) = await SeedCinemaWithPerformanceAsync(db);
        var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);

        var service = CreateService(db, client, breaker: breaker);
        var outcome = await service.SeedRoomsForCinemaAsync(cinema, CancellationToken.None);

        Assert.Equal(RoomSeedOutcome.CircuitOpen, outcome);
        Assert.True(breaker.IsTripped);
    }

    [Fact]
    public async Task Anomalous_widget_config_response_trips_breaker_and_returns_CircuitOpen()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetWidgetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new KinoheldWidgetConfigResult.Anomalous("garbage body"));
        var breaker = new KinoheldCircuitBreaker();

        await using var db = new CineScoutDbContext(options);
        var (cinemaId, _) = await SeedCinemaWithPerformanceAsync(db);
        var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);

        var service = CreateService(db, client, breaker: breaker);
        var outcome = await service.SeedRoomsForCinemaAsync(cinema, CancellationToken.None);

        Assert.Equal(RoomSeedOutcome.CircuitOpen, outcome);
        Assert.True(breaker.IsTripped);
    }

    [Fact]
    public async Task Second_attempt_within_cooldown_returns_CooldownActive_without_calling_out()
    {
        var options = BuildOptions();
        var client = Substitute.For<IKinoheldClient>();
        client.GetWidgetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new KinoheldWidgetConfigResult.Success(ThreeAuditoriumConfig()));
        var cooldownTracker = new KinoheldRoomSeedCooldownTracker();

        await using var db = new CineScoutDbContext(options);
        var (cinemaId, _) = await SeedCinemaWithPerformanceAsync(db);
        var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);

        var service = CreateService(db, client, cooldownTracker: cooldownTracker);
        var first = await service.SeedRoomsForCinemaAsync(cinema, CancellationToken.None);
        var second = await service.SeedRoomsForCinemaAsync(cinema, CancellationToken.None);

        Assert.Equal(RoomSeedOutcome.Seeded, first);
        Assert.Equal(RoomSeedOutcome.CooldownActive, second);
        await client.Received(1).GetWidgetConfigAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
