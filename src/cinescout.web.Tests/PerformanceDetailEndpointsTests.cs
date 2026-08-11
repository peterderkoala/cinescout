using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using cinescout.contracts;
using cinescout.core.Kinoheld;
using cinescout.model;
using cinescout.persistence;
using cinescout.web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;

namespace cinescout.web.Tests;

public sealed partial class PerformanceDetailEndpointsTests : IClassFixture<PerformanceDetailEndpointsTestFactory>
{
    private readonly PerformanceDetailEndpointsTestFactory _factory;

    public PerformanceDetailEndpointsTests(PerformanceDetailEndpointsTestFactory factory) => _factory = factory;

    [Fact]
    public async Task GetDetail_UnknownId_Returns404()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/performances/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_ReturnsFeaturedCardAndSeats_WithMatchReasonsAndStatusBadge()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

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

            AddFreeAdjacentSeats(db, performanceId, "D", count: 4);
            db.PerformancePriceAreas.Add(new PerformancePriceArea { PerformanceId = performanceId, ProviderId = "p1", Name = "Standard", OrderPrice = 9.50m });
            await db.SaveChangesAsync();

            db.Matches.Add(new cinescout.model.Match
            {
                PerformanceId = performanceId,
                TrackedMovieId = trackedMovie.Id,
                MatchedAt = DateTimeOffset.UtcNow,
                Status = MatchStatus.Active,
                HasSufficientSeats = true,
            });
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<PerformanceDetailDto>($"/api/performances/{performanceId}");

        Assert.NotNull(dto);
        Assert.Equal("Perfect Days", dto.Card.Title);
        Assert.Equal("Kino am Rathaus", dto.Card.Cinema);
        Assert.Equal("Saal 2", dto.Card.Room);
        Assert.Equal("€9.50", dto.Card.Price);
        Assert.Equal(PerformanceCardStatus.Available, dto.Card.Status);
        Assert.True(dto.Card.Tracking);
        Assert.True(dto.Card.IsMatch);
        Assert.Contains("2 seats free", dto.Card.MatchReasons);
        Assert.Contains("Fri evening", dto.Card.MatchReasons);
        Assert.Equal(4, dto.Seats.Count);
        Assert.All(dto.Seats, s => Assert.Equal(cinescout.contracts.SeatOccupancyStatus.Free, s.Status));
    }

    [Fact]
    public async Task GetDetail_NoRoomResolvedYet_ReturnsEmptySeatsAndNoStatusBadge()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        int performanceId;
        await using (var db = _factory.CreateDbContext())
        {
            var cinema = await AddCinemaAsync(db, "Kino am Rathaus");
            var film = await AddFilmAsync(db, "Nosferatu", cinema.Id);

            var performance = new Performance
            {
                FilmId = film.Id,
                CinemaId = cinema.Id,
                RoomId = null,
                SourcePerformanceId = "2",
                StartsAt = DateTimeOffset.UtcNow.AddDays(1),
                BookingLink = "https://example.invalid/book/2",
                Status = PerformanceStatus.Normal,
                IsSoldOut = false,
                IsBookable = true,
                LastSeenAt = DateTimeOffset.UtcNow,
            };
            db.Performances.Add(performance);
            await db.SaveChangesAsync();
            performanceId = performance.Id;
        }

        var dto = await client.GetFromJsonAsync<PerformanceDetailDto>($"/api/performances/{performanceId}");

        Assert.NotNull(dto);
        Assert.Equal("", dto.Card.Room);
        Assert.Equal(PerformanceCardStatus.None, dto.Card.Status);
        Assert.False(dto.Card.Tracking);
        Assert.False(dto.Card.IsMatch);
        Assert.Empty(dto.Card.MatchReasons);
        Assert.Empty(dto.Seats);
    }

    [Fact]
    public async Task ForceRefresh_CinemaWithoutKinoheldCinemaId_ReturnsProblemDetails409WithoutKinoheldStatus()
    {
        var client = await CreateWritableClientAsync();

        int performanceId;
        await using (var db = _factory.CreateDbContext())
        {
            var cinema = await AddCinemaAsync(db, "Cinema without a Kinoheld id", kinoheldCinemaId: null);
            var film = await AddFilmAsync(db, "Unavailable Outcome Film", cinema.Id);
            var performance = new Performance
            {
                FilmId = film.Id,
                CinemaId = cinema.Id,
                RoomId = null,
                SourcePerformanceId = "unavailable-1",
                StartsAt = DateTimeOffset.UtcNow.AddDays(1),
                BookingLink = "https://example.invalid/book/unavailable-1",
                Status = PerformanceStatus.Normal,
                IsSoldOut = false,
                IsBookable = true,
                LastSeenAt = DateTimeOffset.UtcNow,
            };
            db.Performances.Add(performance);
            await db.SaveChangesAsync();
            performanceId = performance.Id;
        }

        var response = await client.PostAsync($"/api/performances/{performanceId}/force-refresh", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("kinoheldStatus", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForceRefresh_UnknownId_Returns404()
    {
        var client = await CreateWritableClientAsync();

        var response = await client.PostAsync("/api/performances/999999/force-refresh", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<HttpClient> CreateWritableClientAsync()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var homeHtml = await client.GetStringAsync("/");
        var token = ExtractAntiforgeryToken(homeHtml);
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);

        return client;
    }

    private const DaysOfWeekFlags AllDays = DaysOfWeekFlags.Monday | DaysOfWeekFlags.Tuesday | DaysOfWeekFlags.Wednesday
        | DaysOfWeekFlags.Thursday | DaysOfWeekFlags.Friday | DaysOfWeekFlags.Saturday | DaysOfWeekFlags.Sunday;

    private static DateTimeOffset NextFriday(int hour, int minute)
    {
        var berlinNow = cinescout.core.Matching.CinemaTimeZone.ToLocal(DateTimeOffset.UtcNow);
        var daysUntilFriday = ((int)DayOfWeek.Friday - (int)berlinNow.DayOfWeek + 7) % 7;
        var friday = berlinNow.Date.AddDays(daysUntilFriday == 0 ? 7 : daysUntilFriday).AddHours(hour).AddMinutes(minute);
        var berlinLocal = new DateTimeOffset(DateTime.SpecifyKind(friday, DateTimeKind.Unspecified), cinescout.core.Matching.CinemaTimeZone.Berlin.GetUtcOffset(friday));
        return berlinLocal.ToUniversalTime();
    }

    internal static async Task<Cinema> AddCinemaAsync(CineScoutDbContext db, string name, string? kinoheldCinemaId = "2135")
    {
        var cinema = new Cinema { Name = name, ExternalCinemaId = Guid.NewGuid().ToString(), CrawlBaseUrl = "https://example.invalid", IsActive = true, KinoheldCinemaId = kinoheldCinemaId };
        db.Cinemas.Add(cinema);
        await db.SaveChangesAsync();
        return cinema;
    }

    internal static async Task<Room> AddRoomAsync(CineScoutDbContext db, int cinemaId, string name, string? externalAuditoriumId = null)
    {
        var room = new Room { CinemaId = cinemaId, ExternalAuditoriumId = externalAuditoriumId ?? Guid.NewGuid().ToString(), Name = name };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return room;
    }

    internal static async Task<Film> AddFilmAsync(CineScoutDbContext db, string title, int? cinemaId = null)
    {
        var resolvedCinemaId = cinemaId ?? (await AddCinemaAsync(db, $"Cinema for {title}")).Id;
        var film = new Film { CinemaId = resolvedCinemaId, ExternalFilmId = Guid.NewGuid().ToString(), Title = title };
        db.Films.Add(film);
        await db.SaveChangesAsync();
        return film;
    }

    internal static void AddFreeAdjacentSeats(CineScoutDbContext db, int performanceId, string row, int count)
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
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = AntiforgeryTokenMetaTagRegex().Match(html);
        Assert.True(match.Success, "Expected an antiforgery-token <meta> tag in the response HTML.");

        return match.Groups[1].Value;
    }

    [GeneratedRegex("""<meta name="antiforgery-token" content="([^"]+)"\s*/>""")]
    private static partial Regex AntiforgeryTokenMetaTagRegex();
}

/// <summary>
/// The four-outcome table's non-breaker cases (Fetched/NotBookable/NotFound/CooldownActive), driven
/// through the shared factory's <see cref="FakeKinoheldClient"/> — own factory instance (xUnit
/// gives each test class its own <see cref="PerformanceDetailEndpointsTestFactory"/> even though
/// it's the same type, same as <c>CinemasEndpointsTests</c>/<c>CinemasEndpointsKinoheldStatusTests</c>)
/// so setting <see cref="FakeKinoheldClient.NextGetSeatsResult"/> here doesn't affect
/// <see cref="PerformanceDetailEndpointsTests"/>' shared fixture.
/// </summary>
public sealed partial class PerformanceDetailEndpointsFakeKinoheldTests : IClassFixture<PerformanceDetailEndpointsTestFactory>
{
    private readonly PerformanceDetailEndpointsTestFactory _factory;

    public PerformanceDetailEndpointsFakeKinoheldTests(PerformanceDetailEndpointsTestFactory factory) => _factory = factory;

    [Fact]
    public async Task ForceRefresh_Fetched_ReturnsUpdatedSeatsAndKinoheldStatus200()
    {
        await _factory.ResetDatabaseAsync();
        var client = await CreateWritableClientAsync();

        int performanceId;
        await using (var db = _factory.CreateDbContext())
        {
            var cinema = await PerformanceDetailEndpointsTests.AddCinemaAsync(db, "Kino am Rathaus");
            var room = await PerformanceDetailEndpointsTests.AddRoomAsync(db, cinema.Id, "Saal 1", externalAuditoriumId: "sector-1");
            var film = await PerformanceDetailEndpointsTests.AddFilmAsync(db, "Fetched Outcome Film", cinema.Id);

            var performance = new Performance
            {
                FilmId = film.Id,
                CinemaId = cinema.Id,
                RoomId = room.Id,
                SourcePerformanceId = "fetched-1",
                StartsAt = DateTimeOffset.UtcNow.AddDays(1),
                BookingLink = "https://example.invalid/book/fetched-1",
                Status = PerformanceStatus.Normal,
                IsSoldOut = false,
                IsBookable = true,
                LastSeenAt = DateTimeOffset.UtcNow,
            };
            db.Performances.Add(performance);
            await db.SaveChangesAsync();
            performanceId = performance.Id;
        }

        _factory.FakeKinoheldClient.NextGetSeatsResult = (_, _) => new KinoheldSeatsResult.Success(
            RawPayload: "{\"seats\":{}}",
            Seats: [new KinoheldSeat("A1", "A", 1, "sf", null, null, "sector-1", PriceAreaProviderId: null)],
            PriceAreas: []);

        var response = await client.PostAsync($"/api/performances/{performanceId}/force-refresh", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PerformanceDetailDto>();
        Assert.NotNull(dto);
        var seat = Assert.Single(dto.Seats);
        Assert.Equal("A1", seat.SourceSeatId);
        Assert.Equal(cinescout.contracts.SeatOccupancyStatus.Free, seat.Status);
    }

    [Fact]
    public async Task ForceRefresh_NotBookable_ReturnsProblemDetails409WithNotBookableKinoheldStatus()
    {
        await _factory.ResetDatabaseAsync();
        var client = await CreateWritableClientAsync();
        var performanceId = await SeedPerformanceAsync("not-bookable-1");

        _factory.FakeKinoheldClient.NextGetSeatsResult = (_, _) => new KinoheldSeatsResult.NotBookable();

        var response = await client.PostAsync($"/api/performances/{performanceId}/force-refresh", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"kinoheldStatus\":\"not-bookable\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForceRefresh_Gone_ReturnsProblemDetails410WithGoneKinoheldStatus()
    {
        await _factory.ResetDatabaseAsync();
        var client = await CreateWritableClientAsync();
        var performanceId = await SeedPerformanceAsync("gone-1");

        _factory.FakeKinoheldClient.NextGetSeatsResult = (_, _) => new KinoheldSeatsResult.NotFound();

        var response = await client.PostAsync($"/api/performances/{performanceId}/force-refresh", content: null);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"kinoheldStatus\":\"gone\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ForceRefresh_CalledAgainWithinCooldown_ReturnsProblemDetails429WithCooldownKinoheldStatus()
    {
        await _factory.ResetDatabaseAsync();
        var client = await CreateWritableClientAsync();
        var performanceId = await SeedPerformanceAsync("cooldown-1");

        _factory.FakeKinoheldClient.NextGetSeatsResult = (_, _) => new KinoheldSeatsResult.Success("{\"seats\":{}}", [], []);

        var first = await client.PostAsync($"/api/performances/{performanceId}/force-refresh", content: null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsync($"/api/performances/{performanceId}/force-refresh", content: null);

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        var body = await second.Content.ReadAsStringAsync();
        Assert.Contains("\"kinoheldStatus\":\"cooldown\"", body, StringComparison.Ordinal);
    }

    private async Task<int> SeedPerformanceAsync(string sourcePerformanceId)
    {
        await using var db = _factory.CreateDbContext();
        var cinema = await PerformanceDetailEndpointsTests.AddCinemaAsync(db, $"Cinema for {sourcePerformanceId}");
        var film = await PerformanceDetailEndpointsTests.AddFilmAsync(db, $"Film for {sourcePerformanceId}", cinema.Id);

        var performance = new Performance
        {
            FilmId = film.Id,
            CinemaId = cinema.Id,
            RoomId = null,
            SourcePerformanceId = sourcePerformanceId,
            StartsAt = DateTimeOffset.UtcNow.AddDays(1),
            BookingLink = $"https://example.invalid/book/{sourcePerformanceId}",
            Status = PerformanceStatus.Normal,
            IsSoldOut = false,
            IsBookable = true,
            LastSeenAt = DateTimeOffset.UtcNow,
        };
        db.Performances.Add(performance);
        await db.SaveChangesAsync();
        return performance.Id;
    }

    private async Task<HttpClient> CreateWritableClientAsync()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var homeHtml = await client.GetStringAsync("/");
        var token = AntiforgeryTokenMetaTagRegex().Match(homeHtml).Groups[1].Value;
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);

        return client;
    }

    [GeneratedRegex("""<meta name="antiforgery-token" content="([^"]+)"\s*/>""")]
    private static partial Regex AntiforgeryTokenMetaTagRegex();
}

/// <summary>
/// Separate test class, own factory instance (own Postgres container, own DI container):
/// <see cref="KinoheldCircuitBreaker"/> is a permanent one-way trip with no reset, so tripping it
/// here must not leak into either of the other two test classes' shared fixtures — same isolation
/// <c>CinemasEndpointsKinoheldStatusTests</c> already established for the same reason.
/// </summary>
public sealed partial class PerformanceDetailEndpointsBreakerTests : IClassFixture<PerformanceDetailEndpointsTestFactory>
{
    private readonly PerformanceDetailEndpointsTestFactory _factory;

    public PerformanceDetailEndpointsBreakerTests(PerformanceDetailEndpointsTestFactory factory) => _factory = factory;

    [Fact]
    public async Task ForceRefresh_WhileBreakerTripped_ReturnsProblemDetails503WithBreakerOpenKinoheldStatus()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();
        var homeHtml = await client.GetStringAsync("/");
        var token = AntiforgeryTokenMetaTagRegex().Match(homeHtml).Groups[1].Value;
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token);

        int performanceId;
        await using (var db = _factory.CreateDbContext())
        {
            var cinema = await PerformanceDetailEndpointsTests.AddCinemaAsync(db, "Breaker Test Cinema");
            var film = await PerformanceDetailEndpointsTests.AddFilmAsync(db, "Breaker Test Film", cinema.Id);
            var performance = new Performance
            {
                FilmId = film.Id,
                CinemaId = cinema.Id,
                RoomId = null,
                SourcePerformanceId = "breaker-1",
                StartsAt = DateTimeOffset.UtcNow.AddDays(1),
                BookingLink = "https://example.invalid/book/breaker-1",
                Status = PerformanceStatus.Normal,
                IsSoldOut = false,
                IsBookable = true,
                LastSeenAt = DateTimeOffset.UtcNow,
            };
            db.Performances.Add(performance);
            await db.SaveChangesAsync();
            performanceId = performance.Id;
        }

        var breaker = _factory.Services.GetRequiredService<KinoheldCircuitBreaker>();
        breaker.Trip("test-forced-trip");

        var response = await client.PostAsync($"/api/performances/{performanceId}/force-refresh", content: null);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"kinoheldStatus\":\"breaker-open\"", body, StringComparison.Ordinal);
    }

    [GeneratedRegex("""<meta name="antiforgery-token" content="([^"]+)"\s*/>""")]
    private static partial Regex AntiforgeryTokenMetaTagRegex();
}

/// <summary>
/// Test double swapped in for the real HTTP-backed <see cref="IKinoheldClient"/>, registered in
/// every test class's factory instance (see <see cref="PerformanceDetailEndpointsTestFactory"/>) —
/// GET's own view-triggered <c>FetchForPerformanceAsync(forceRefresh: false, ...)</c> call would
/// otherwise attempt a real outbound Kinoheld request in every GET test whose seeded Cinema has a
/// <c>KinoheldCinemaId</c> and no fresh <c>SeatingSnapshot</c>. Defaults to the inert
/// <see cref="KinoheldSeatsResult.NotBookable"/> (logged and skipped, no DB writes) so tests that
/// don't care about the Kinoheld outcome are unaffected; tests that do set
/// <see cref="NextGetSeatsResult"/> explicitly before exercising it.
/// </summary>
public sealed class FakeKinoheldClient : IKinoheldClient
{
    public Func<string, string, KinoheldSeatsResult>? NextGetSeatsResult { get; set; }

    public Task<KinoheldWidgetConfigResult> GetWidgetConfigAsync(string bookingLink, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Not exercised by PerformanceDetailEndpoints tests.");

    public Task<KinoheldSeatsResult> GetSeatsAsync(string cinemaId, string showId, CancellationToken cancellationToken) =>
        Task.FromResult(NextGetSeatsResult?.Invoke(cinemaId, showId) ?? new KinoheldSeatsResult.NotBookable());
}

public sealed class PerformanceDetailEndpointsTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string CorrectPassword = "correct-horse-battery-staple";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18")
        .Build();

    public FakeKinoheldClient FakeKinoheldClient { get; } = new();

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

    /// <summary>Tests share one Postgres container/schema; clear out everything Performance Detail's read model touches between tests.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var db = CreateDbContext();
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
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IKinoheldClient>();
            services.AddSingleton<IKinoheldClient>(FakeKinoheldClient);
        });
    }
}
