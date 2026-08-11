using System.Net.Http.Json;
using cinescout.contracts;
using cinescout.model;
using cinescout.persistence;
using cinescout.web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace cinescout.web.Tests;

public sealed class HomeEndpointsTests : IClassFixture<HomeEndpointsTestFactory>
{
    private const DaysOfWeekFlags AllDays = DaysOfWeekFlags.Monday | DaysOfWeekFlags.Tuesday | DaysOfWeekFlags.Wednesday
        | DaysOfWeekFlags.Thursday | DaysOfWeekFlags.Friday | DaysOfWeekFlags.Saturday | DaysOfWeekFlags.Sunday;

    private readonly HomeEndpointsTestFactory _factory;

    public HomeEndpointsTests(HomeEndpointsTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_NoMatchesAndNoTrackedMovies_ReturnsEmptyPanelsAndHasAnyTrackedMovieFalse()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var dto = await client.GetFromJsonAsync<HomePageDto>("/api/home");

        Assert.NotNull(dto);
        Assert.Empty(dto.ActiveMatches);
        Assert.False(dto.HasAnyTrackedMovie);
        Assert.Empty(dto.TrackedMovies);
        Assert.Empty(dto.RecentActivity);
    }

    [Fact]
    public async Task Get_NoMatchesButFilmsTracked_ReturnsHasAnyTrackedMovieTrue()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var film = await AddFilmAsync(db, "Poor Things");
            db.TrackedMovies.Add(new TrackedMovie { FilmId = film.Id, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<HomePageDto>("/api/home");

        Assert.NotNull(dto);
        Assert.Empty(dto.ActiveMatches);
        Assert.True(dto.HasAnyTrackedMovie);
    }

    [Fact]
    public async Task Get_ActiveMatch_ReturnsFeaturedCardWithPriceAndMatchReasons()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        // A Friday at 20:15 local time, comfortably in the future — falls in the "evening" bucket
        // (17:00-20:59) per HomeEndpoints' DescribeDayPart.
        var startsAt = NextFriday(hour: 20, minute: 15);

        int performanceId;
        await using (var db = _factory.CreateDbContext())
        {
            var cinema = await AddCinemaAsync(db, "Kino am Rathaus");
            var room = await AddRoomAsync(db, cinema.Id, "Saal 2");
            var film = await AddFilmAsync(db, "Perfect Days", cinema.Id);
            var trackedMovie = new TrackedMovie { FilmId = film.Id, CreatedAt = DateTimeOffset.UtcNow };
            db.TrackedMovies.Add(trackedMovie);
            await db.SaveChangesAsync();

            var performance = new Performance
            {
                FilmId = film.Id,
                CinemaId = cinema.Id,
                RoomId = room.Id,
                SourcePerformanceId = "1",
                StartsAt = startsAt,
                BookingLink = "https://example.invalid/book/1",
                Status = PerformanceStatus.Normal,
                IsSoldOut = false,
                IsBookable = true,
                LastSeenAt = DateTimeOffset.UtcNow,
            };
            db.Performances.Add(performance);
            await db.SaveChangesAsync();
            performanceId = performance.Id;

            db.FavoriteTimeWindows.Add(new FavoriteTimeWindow { DaysOfWeek = AllDays, StartTime = TimeOnly.MinValue, EndTime = TimeOnly.MaxValue });
            db.FavoriteSeatMatrices.Add(new FavoriteSeatMatrix
            {
                RoomId = room.Id,
                FilmId = null,
                Name = "General",
                RowStart = "A",
                RowEnd = "Z",
                SeatNumberStart = 1,
                SeatNumberEnd = 99,
                PartySize = 2,
                IsEnabled = true,
            });
            await db.SaveChangesAsync();

            AddFreeAdjacentSeats(db, performanceId, "D", count: 4, priceAreaProviderId: "p1");
            db.PerformancePriceAreas.Add(new PerformancePriceArea { PerformanceId = performanceId, ProviderId = "p1", Name = "Standard", OrderPrice = 9.50m });
            await db.SaveChangesAsync();

            db.Matches.Add(new Match
            {
                PerformanceId = performanceId,
                TrackedMovieId = trackedMovie.Id,
                MatchedAt = DateTimeOffset.UtcNow,
                Status = MatchStatus.Active,
                HasSufficientSeats = true,
            });
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<HomePageDto>("/api/home");

        Assert.NotNull(dto);
        var card = Assert.Single(dto.ActiveMatches);
        Assert.Equal("Perfect Days", card.Title);
        Assert.Equal("Kino am Rathaus", card.Cinema);
        Assert.Equal("Saal 2", card.Room);
        Assert.Equal("€9.50", card.Price);
        Assert.Equal(PerformanceCardStatus.Available, card.Status);
        Assert.True(card.Tracking);
        Assert.True(card.IsMatch);
        // SeatBlockFinder stops at the first run that satisfies PartySize (2), not the full
        // 4-seat contiguous block — matches production matching-engine behavior exactly.
        Assert.Contains("2 seats free", card.MatchReasons);
        Assert.Contains("Fri evening", card.MatchReasons);
    }

    [Fact]
    public async Task Get_TrackedMoviesPanel_OrdersByTitle_CapsAtFive_AndReportsNoUpcomingPerformances()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        string[] titles = ["Anatomy of a Fall", "Nosferatu", "Past Lives", "Perfect Days", "Poor Things", "The Zone of Interest"];
        await using (var db = _factory.CreateDbContext())
        {
            foreach (var title in titles)
            {
                var film = await AddFilmAsync(db, title);
                db.TrackedMovies.Add(new TrackedMovie { FilmId = film.Id, CreatedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();

                // "Past Lives" deliberately gets no upcoming performance.
                if (title != "Past Lives")
                {
                    db.Performances.Add(new Performance
                    {
                        FilmId = film.Id,
                        CinemaId = film.CinemaId,
                        RoomId = null,
                        SourcePerformanceId = $"perf-{title}",
                        StartsAt = DateTimeOffset.UtcNow.AddDays(1),
                        BookingLink = "https://example.invalid/book",
                        Status = PerformanceStatus.Normal,
                        IsSoldOut = false,
                        IsBookable = true,
                        LastSeenAt = DateTimeOffset.UtcNow,
                    });
                    await db.SaveChangesAsync();
                }
            }
        }

        var dto = await client.GetFromJsonAsync<HomePageDto>("/api/home");

        Assert.NotNull(dto);
        Assert.Equal(5, dto.TrackedMovies.Count);
        Assert.Equal(titles.Order().Take(5), dto.TrackedMovies.Select(r => r.FilmTitle));

        var pastLives = dto.TrackedMovies.SingleOrDefault(r => r.FilmTitle == "Past Lives");
        Assert.NotNull(pastLives);
        Assert.Null(pastLives.NextPerformance);
    }

    [Fact]
    public async Task Get_RecentActivity_ReturnsLastFiveOrderedByMostRecent_ExcludingRowsWithoutFilmId()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var oldest = await AddFilmAsync(db, "The Holdovers");
            var middle = await AddFilmAsync(db, "Past Lives");
            var newest = await AddFilmAsync(db, "Poor Things");
            var noFilm = await AddFilmAsync(db, "Legacy Row (pre-migration, no FilmId)");

            db.NotificationLogs.AddRange(
                new NotificationLog { NotificationType = NotificationType.TrackStarted, FilmId = oldest.Id, Channel = "Discord", SentAt = DateTimeOffset.UtcNow.AddDays(-5), Status = NotificationStatus.Success },
                new NotificationLog { NotificationType = NotificationType.TrackStarted, FilmId = middle.Id, Channel = "Discord", SentAt = DateTimeOffset.UtcNow.AddDays(-3), Status = NotificationStatus.Success },
                new NotificationLog { NotificationType = NotificationType.TrackStarted, FilmId = newest.Id, Channel = "Discord", SentAt = DateTimeOffset.UtcNow.AddHours(-2), Status = NotificationStatus.Success },
                new NotificationLog { NotificationType = NotificationType.NewFilmAdded, FilmId = null, Channel = "Discord", SentAt = DateTimeOffset.UtcNow.AddMinutes(-1), Status = NotificationStatus.Success });
            _ = noFilm;
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<HomePageDto>("/api/home");

        Assert.NotNull(dto);
        Assert.Equal(["Poor Things", "Past Lives", "The Holdovers"], dto.RecentActivity.Select(a => a.FilmTitle));
    }

    private static DateTimeOffset NextFriday(int hour, int minute)
    {
        var berlinNow = cinescout.core.Matching.CinemaTimeZone.ToLocal(DateTimeOffset.UtcNow);
        var daysUntilFriday = ((int)DayOfWeek.Friday - (int)berlinNow.DayOfWeek + 7) % 7;
        var friday = berlinNow.Date.AddDays(daysUntilFriday == 0 ? 7 : daysUntilFriday).AddHours(hour).AddMinutes(minute);
        var berlinLocal = new DateTimeOffset(DateTime.SpecifyKind(friday, DateTimeKind.Unspecified), cinescout.core.Matching.CinemaTimeZone.Berlin.GetUtcOffset(friday));
        return berlinLocal.ToUniversalTime();
    }

    private static async Task<Cinema> AddCinemaAsync(CineScoutDbContext db, string name)
    {
        var cinema = new Cinema { Name = name, ExternalCinemaId = Guid.NewGuid().ToString(), CrawlBaseUrl = "https://example.invalid", IsActive = true };
        db.Cinemas.Add(cinema);
        await db.SaveChangesAsync();
        return cinema;
    }

    private static async Task<Room> AddRoomAsync(CineScoutDbContext db, int cinemaId, string name)
    {
        var room = new Room { CinemaId = cinemaId, ExternalAuditoriumId = Guid.NewGuid().ToString(), Name = name };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return room;
    }

    private static async Task<Film> AddFilmAsync(CineScoutDbContext db, string title, int? cinemaId = null)
    {
        var resolvedCinemaId = cinemaId ?? (await AddCinemaAsync(db, $"Cinema for {title}")).Id;
        var film = new Film { CinemaId = resolvedCinemaId, ExternalFilmId = Guid.NewGuid().ToString(), Title = title };
        db.Films.Add(film);
        await db.SaveChangesAsync();
        return film;
    }

    private static void AddFreeAdjacentSeats(CineScoutDbContext db, int performanceId, string row, int count, string? priceAreaProviderId = null)
    {
        for (var i = 1; i <= count; i++)
        {
            db.SeatStatuses.Add(new SeatStatus
            {
                PerformanceId = performanceId,
                SourceSeatId = $"{row}{i}",
                Row = row,
                SeatNumber = i,
                Status = cinescout.model.SeatOccupancyStatus.Free,
                LeftNeighborSeatId = i > 1 ? $"{row}{i - 1}" : null,
                RightNeighborSeatId = i < count ? $"{row}{i + 1}" : null,
                PriceAreaProviderId = priceAreaProviderId,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
    }
}

public sealed class HomeEndpointsTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string CorrectPassword = "correct-horse-battery-staple";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await DisposeAsync();
    }

    public CineScoutDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CineScoutDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    /// <summary>Tests share one Postgres container/schema; clear out everything Home's read model touches between tests.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var db = CreateDbContext();
        db.NotificationLogs.RemoveRange(db.NotificationLogs);
        db.Matches.RemoveRange(db.Matches);
        db.SeatStatuses.RemoveRange(db.SeatStatuses);
        db.PerformancePriceAreas.RemoveRange(db.PerformancePriceAreas);
        db.FavoriteSeatMatrices.RemoveRange(db.FavoriteSeatMatrices);
        db.FavoriteTimeWindows.RemoveRange(db.FavoriteTimeWindows);
        db.TrackedMovies.RemoveRange(db.TrackedMovies);
        db.Performances.RemoveRange(db.Performances);
        db.Films.RemoveRange(db.Films);
        db.Rooms.RemoveRange(db.Rooms);
        db.Cinemas.RemoveRange(db.Cinemas);
        await db.SaveChangesAsync();
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        await using (var db = CreateDbContext())
        {
            var user = await db.Users.SingleAsync();
            user.PasswordHash = new PasswordHasher<AppUser>().HashPassword(AppUser.Instance, CorrectPassword);
            user.SetupCompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent([new KeyValuePair<string, string>("password", CorrectPassword)]));

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());
    }
}
