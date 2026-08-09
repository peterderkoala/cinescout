using System.Text.Json;
using cinescout.core.Discord;
using cinescout.core.HallOfFame;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Testcontainers.PostgreSql;

namespace cinescout.core.Tests;

public class HallOfFameCrawlServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    private readonly IDiscordNotifier _notifier = CreateNotifier();

    private static IDiscordNotifier CreateNotifier()
    {
        var notifier = Substitute.For<IDiscordNotifier>();
        notifier.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationResult(true, "204 No Content"));
        return notifier;
    }

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

    private static Cinema MakeCinema() => new()
    {
        ExternalCinemaId = "580",
        Name = "HALL OF FAME - Kino in Kamp-Lintfort",
        CrawlBaseUrl = "https://kamp-lintfort.hall-of-fame.website/programm/api/filtered-films",
        IsActive = true,
    };

    private static HallOfFameScheduleResponse MakeSchedule(
        int detailId,
        string filmTitle,
        int performanceId,
        long unixDateTime,
        int isSoldOut = 0,
        string bookingLink = "https://www.kinoheld.de/kino-kamp-lintfort/hall-of-fame?mode=widget&change=no&showId=1",
        int isNotBookable = 0,
        int isOnline = 1,
        int saleIsAllowed = 1) => new()
        {
            Films =
            [
                new HallOfFameFilmDto
                {
                    DetailId = detailId,
                    FilmTitle = filmTitle,
                    PerformanceGroups =
                    [
                        new HallOfFamePerformanceGroupDto
                        {
                            Performances = new Dictionary<string, JsonElement>
                            {
                                [performanceId.ToString()] = JsonSerializer.SerializeToElement(
                                    new HallOfFamePerformanceDto
                                    {
                                        PerformanceId = performanceId,
                                        BookingLink = bookingLink,
                                        UnixDateTime = unixDateTime,
                                        IsSoldOut = isSoldOut,
                                        IsNotBookable = isNotBookable,
                                        IsOnline = isOnline,
                                        SaleIsAllowed = saleIsAllowed,
                                    }),
                            },
                        },
                    ],
                },
            ],
        };

    [Fact]
    public async Task Upsert_creates_film_and_performance_on_first_crawl()
    {
        var options = BuildOptions();
        var now = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

        int cinemaId;
        await using (var setup = new CineScoutDbContext(options))
        {
            var cinema = MakeCinema();
            setup.Cinemas.Add(cinema);
            await setup.SaveChangesAsync();
            cinemaId = cinema.Id;
        }

        var schedule = MakeSchedule(
            detailId: 401865,
            filmTitle: "Vaiana - Live Action",
            performanceId: 74011,
            unixDateTime: 1783969200,
            isSoldOut: 0);

        var client = Substitute.For<IHallOfFameClient>();
        client.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(schedule);

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, _notifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, TimeSpan.FromHours(1), now, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);

        var film = await read.Films.SingleAsync(f => f.CinemaId == cinemaId);
        Assert.Equal("401865", film.ExternalFilmId);
        Assert.Equal("Vaiana - Live Action", film.Title);

        var performance = await read.Performances.SingleAsync(p => p.CinemaId == cinemaId);
        Assert.Equal("74011", performance.SourcePerformanceId);
        Assert.Equal(film.Id, performance.FilmId);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1783969200), performance.StartsAt);
        Assert.Equal(PerformanceStatus.Normal, performance.Status);
        Assert.False(performance.IsSoldOut);
        Assert.True(performance.IsBookable);
        Assert.Equal(now, performance.LastSeenAt);
    }

    [Fact]
    public async Task Upsert_updates_existing_performance_in_place_rather_than_duplicating()
    {
        var options = BuildOptions();
        var firstCrawl = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        var secondCrawl = firstCrawl.AddHours(1);

        int cinemaId;
        await using (var setup = new CineScoutDbContext(options))
        {
            var cinema = MakeCinema();
            setup.Cinemas.Add(cinema);
            await setup.SaveChangesAsync();
            cinemaId = cinema.Id;
        }

        var client = Substitute.For<IHallOfFameClient>();
        client.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                MakeSchedule(401865, "Vaiana - Live Action", 74011, 1783969200, isSoldOut: 0),
                MakeSchedule(401865, "Vaiana - Live Action", 74011, 1783969200, isSoldOut: 1));

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, _notifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, TimeSpan.FromHours(1), firstCrawl, CancellationToken.None);
        }

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, _notifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, TimeSpan.FromHours(1), secondCrawl, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);

        Assert.Single(read.Films.Where(f => f.CinemaId == cinemaId));
        var performance = await read.Performances.SingleAsync(p => p.CinemaId == cinemaId);
        Assert.True(performance.IsSoldOut);
        Assert.Equal(secondCrawl, performance.LastSeenAt);
    }

    [Fact]
    public async Task Performance_missed_for_two_consecutive_crawls_is_cancelled()
    {
        var options = BuildOptions();
        var now = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        var crawlInterval = TimeSpan.FromHours(1);

        int cinemaId, filmId;
        await using (var setup = new CineScoutDbContext(options))
        {
            var cinema = MakeCinema();
            setup.Cinemas.Add(cinema);
            await setup.SaveChangesAsync();
            cinemaId = cinema.Id;

            var film = new Film { CinemaId = cinemaId, ExternalFilmId = "999", Title = "Some Other Film" };
            setup.Films.Add(film);
            await setup.SaveChangesAsync();
            filmId = film.Id;

            setup.Performances.Add(new Performance
            {
                FilmId = filmId,
                CinemaId = cinemaId,
                SourcePerformanceId = "12345",
                StartsAt = now.AddDays(1),
                BookingLink = "https://example.invalid/12345",
                Status = PerformanceStatus.Normal,
                IsSoldOut = false,
                IsBookable = true,
                LastSeenAt = now - TimeSpan.FromHours(3), // missed 2+ intervals
            });
            await setup.SaveChangesAsync();
        }

        // Canned schedule with a DIFFERENT film/performance — the seeded one is absent.
        var schedule = MakeSchedule(401865, "Vaiana - Live Action", 74011, 1783969200, isSoldOut: 0);
        var client = Substitute.For<IHallOfFameClient>();
        client.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(schedule);

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, _notifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, crawlInterval, now, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var missed = await read.Performances.SingleAsync(p => p.SourcePerformanceId == "12345");
        Assert.Equal(PerformanceStatus.Cancelled, missed.Status);
    }

    [Fact]
    public async Task Performance_missed_only_once_stays_normal()
    {
        var options = BuildOptions();
        var now = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        var crawlInterval = TimeSpan.FromHours(1);

        int cinemaId, filmId;
        await using (var setup = new CineScoutDbContext(options))
        {
            var cinema = MakeCinema();
            setup.Cinemas.Add(cinema);
            await setup.SaveChangesAsync();
            cinemaId = cinema.Id;

            var film = new Film { CinemaId = cinemaId, ExternalFilmId = "999", Title = "Some Other Film" };
            setup.Films.Add(film);
            await setup.SaveChangesAsync();
            filmId = film.Id;

            setup.Performances.Add(new Performance
            {
                FilmId = filmId,
                CinemaId = cinemaId,
                SourcePerformanceId = "12345",
                StartsAt = now.AddDays(1),
                BookingLink = "https://example.invalid/12345",
                Status = PerformanceStatus.Normal,
                IsSoldOut = false,
                IsBookable = true,
                LastSeenAt = now - crawlInterval, // missed exactly 1 interval — boundary, stays Normal
            });
            await setup.SaveChangesAsync();
        }

        var schedule = MakeSchedule(401865, "Vaiana - Live Action", 74011, 1783969200, isSoldOut: 0);
        var client = Substitute.For<IHallOfFameClient>();
        client.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(schedule);

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, _notifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, crawlInterval, now, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var stillPresent = await read.Performances.SingleAsync(p => p.SourcePerformanceId == "12345");
        Assert.Equal(PerformanceStatus.Normal, stillPresent.Status);
    }

    [Fact]
    public async Task Snapshot_is_written_unconditionally_on_every_crawl()
    {
        var options = BuildOptions();
        var firstCrawl = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        var secondCrawl = firstCrawl.AddHours(1);

        int cinemaId;
        await using (var setup = new CineScoutDbContext(options))
        {
            var cinema = MakeCinema();
            setup.Cinemas.Add(cinema);
            await setup.SaveChangesAsync();
            cinemaId = cinema.Id;
        }

        // Identical response both crawls — nothing changed, but a snapshot must still be
        // written each time.
        var schedule = MakeSchedule(401865, "Vaiana - Live Action", 74011, 1783969200, isSoldOut: 0);
        var client = Substitute.For<IHallOfFameClient>();
        client.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(schedule);

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, _notifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, TimeSpan.FromHours(1), firstCrawl, CancellationToken.None);
        }

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, _notifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, TimeSpan.FromHours(1), secondCrawl, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var performance = await read.Performances.SingleAsync(p => p.CinemaId == cinemaId);
        var snapshots = await read.PerformanceSnapshots
            .Where(s => s.PerformanceId == performance.Id)
            .ToListAsync();

        Assert.Equal(2, snapshots.Count);
        Assert.Contains(snapshots, s => s.CrawledAt == firstCrawl);
        Assert.Contains(snapshots, s => s.CrawledAt == secondCrawl);
    }

    [Fact]
    public async Task Snapshot_RawPayload_preserves_fields_the_narrowed_DTO_does_not_model()
    {
        var options = BuildOptions();
        var now = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

        int cinemaId;
        await using (var setup = new CineScoutDbContext(options))
        {
            var cinema = MakeCinema();
            setup.Cinemas.Add(cinema);
            await setup.SaveChangesAsync();
            cinemaId = cinema.Id;
        }

        // A field ("performanceAuditoriumAttributeTitle", real upstream data) that
        // HallOfFamePerformanceDto deliberately does not model — the archival snapshot must
        // still retain it, since RawPayload is meant to be the real upstream response, not a
        // re-serialization of only the fields this DTO happens to map.
        const string rawPerformanceJson = """
            {
                "performanceID": 74011,
                "bookingLink": "https://www.kinoheld.de/kino-kamp-lintfort/hall-of-fame?mode=widget&change=no&showId=74011",
                "unixdatetime": 1783969200,
                "isSoldOut": 0,
                "isNotBookable": 0,
                "isOnline": 1,
                "saleIsAllowed": 1,
                "performanceAuditoriumAttributeTitle": "D-Box"
            }
            """;
        var performanceElement = JsonDocument.Parse(rawPerformanceJson).RootElement;

        var schedule = new HallOfFameScheduleResponse
        {
            Films =
            [
                new HallOfFameFilmDto
                {
                    DetailId = 401865,
                    FilmTitle = "Vaiana - Live Action",
                    PerformanceGroups =
                    [
                        new HallOfFamePerformanceGroupDto
                        {
                            Performances = new Dictionary<string, JsonElement> { ["74011"] = performanceElement },
                        },
                    ],
                },
            ],
        };

        var client = Substitute.For<IHallOfFameClient>();
        client.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(schedule);

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, _notifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, TimeSpan.FromHours(1), now, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var performance = await read.Performances.SingleAsync(p => p.CinemaId == cinemaId);
        var snapshot = await read.PerformanceSnapshots.SingleAsync(s => s.PerformanceId == performance.Id);

        Assert.Contains("performanceAuditoriumAttributeTitle", snapshot.RawPayload);
        Assert.Contains("D-Box", snapshot.RawPayload);
    }

    [Fact]
    public async Task Film_with_null_detailId_is_skipped_without_crashing_the_rest_of_the_crawl()
    {
        var options = BuildOptions();
        var now = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

        int cinemaId;
        await using (var setup = new CineScoutDbContext(options))
        {
            var cinema = MakeCinema();
            setup.Cinemas.Add(cinema);
            await setup.SaveChangesAsync();
            cinemaId = cinema.Id;
        }

        var schedule = new HallOfFameScheduleResponse
        {
            Films =
            [
                new HallOfFameFilmDto
                {
                    DetailId = null,
                    FilmTitle = "Untitled Draft Entry",
                    PerformanceGroups = [],
                },
                new HallOfFameFilmDto
                {
                    DetailId = 401865,
                    FilmTitle = "Vaiana - Live Action",
                    PerformanceGroups =
                    [
                        new HallOfFamePerformanceGroupDto
                        {
                            Performances = new Dictionary<string, JsonElement>
                            {
                                ["74011"] = JsonSerializer.SerializeToElement(
                                    new HallOfFamePerformanceDto
                                    {
                                        PerformanceId = 74011,
                                        BookingLink = "https://www.kinoheld.de/kino-kamp-lintfort/hall-of-fame?mode=widget&change=no&showId=74011",
                                        UnixDateTime = 1783969200,
                                    }),
                            },
                        },
                    ],
                },
            ],
        };

        var client = Substitute.For<IHallOfFameClient>();
        client.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(schedule);

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, _notifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, TimeSpan.FromHours(1), now, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var film = await read.Films.SingleAsync(f => f.CinemaId == cinemaId);
        Assert.Equal("Vaiana - Live Action", film.Title);
        Assert.DoesNotContain(read.Films, f => f.Title == "Untitled Draft Entry");
    }

    [Fact]
    public async Task First_time_seeing_a_film_sends_and_logs_a_NewFilmAdded_notification()
    {
        var options = BuildOptions();
        var now = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

        int cinemaId;
        await using (var setup = new CineScoutDbContext(options))
        {
            var cinema = MakeCinema();
            setup.Cinemas.Add(cinema);
            await setup.SaveChangesAsync();
            cinemaId = cinema.Id;
        }

        var schedule = MakeSchedule(401865, "Vaiana - Live Action", 74011, 1783969200, isSoldOut: 0);
        var client = Substitute.For<IHallOfFameClient>();
        client.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(schedule);

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, _notifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, TimeSpan.FromHours(1), now, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var log = await read.NotificationLogs.SingleAsync();
        Assert.Equal(NotificationType.NewFilmAdded, log.NotificationType);
        Assert.Null(log.MatchId);
        Assert.Equal("Discord", log.Channel);
        Assert.Equal(NotificationStatus.Success, log.Status);

        await _notifier.Received(1).SendAsync(
            Arg.Is<string>(m => m != null && m.Contains("Vaiana - Live Action")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recrawling_an_already_known_film_does_not_re_notify()
    {
        var options = BuildOptions();
        var firstCrawl = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
        var secondCrawl = firstCrawl.AddHours(1);

        int cinemaId;
        await using (var setup = new CineScoutDbContext(options))
        {
            var cinema = MakeCinema();
            setup.Cinemas.Add(cinema);
            await setup.SaveChangesAsync();
            cinemaId = cinema.Id;
        }

        var client = Substitute.For<IHallOfFameClient>();
        client.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(
                MakeSchedule(401865, "Vaiana - Live Action", 74011, 1783969200, isSoldOut: 0),
                MakeSchedule(401865, "Vaiana - Live Action", 74011, 1783969200, isSoldOut: 1));

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, _notifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, TimeSpan.FromHours(1), firstCrawl, CancellationToken.None);
        }

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, _notifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, TimeSpan.FromHours(1), secondCrawl, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.Equal(1, await read.NotificationLogs.CountAsync(l => l.NotificationType == NotificationType.NewFilmAdded));

        await _notifier.Received(1).SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Notifier_failure_still_logs_the_NewFilmAdded_attempt_without_rolling_back_the_upsert()
    {
        var options = BuildOptions();
        var now = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

        int cinemaId;
        await using (var setup = new CineScoutDbContext(options))
        {
            var cinema = MakeCinema();
            setup.Cinemas.Add(cinema);
            await setup.SaveChangesAsync();
            cinemaId = cinema.Id;
        }

        var schedule = MakeSchedule(401865, "Vaiana - Live Action", 74011, 1783969200, isSoldOut: 0);
        var client = Substitute.For<IHallOfFameClient>();
        client.GetScheduleAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(schedule);

        var failingNotifier = Substitute.For<IDiscordNotifier>();
        failingNotifier.SendAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new NotificationResult(false, "Discord webhook URL not configured."));

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new HallOfFameCrawlService(db, client, failingNotifier);
            var cinema = await db.Cinemas.SingleAsync(s => s.Id == cinemaId);
            await service.CrawlCinemaAsync(cinema, TimeSpan.FromHours(1), now, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.True(await read.Films.AnyAsync(f => f.CinemaId == cinemaId));

        var log = await read.NotificationLogs.SingleAsync();
        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Equal("Discord webhook URL not configured.", log.ResponseDetail);
    }
}
