using System.Net.Http.Json;
using cinescout.contracts;
using cinescout.core.Matching;
using cinescout.model;
using cinescout.persistence;
using cinescout.web.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace cinescout.web.Tests;

public sealed class ScheduleEndpointsTests : IClassFixture<ScheduleEndpointsTestFactory>
{
    private readonly ScheduleEndpointsTestFactory _factory;

    public ScheduleEndpointsTests(ScheduleEndpointsTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_NoPerformances_ReturnsEmptyWindowCopyAndCorrectWeekLabel()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var dto = await client.GetFromJsonAsync<ScheduleDto>("/api/schedule");

        Assert.NotNull(dto);
        Assert.Empty(dto.Days);
        Assert.Equal(0, dto.WeekOffset);

        var today = DateOnly.FromDateTime(CinemaTimeZone.ToLocal(DateTimeOffset.UtcNow).DateTime);
        var expected = PerformanceDateTimeFormatting.FormatWeekCaption(today, today.AddDays(13));
        Assert.Equal(expected, dto.WeekLabel);
    }

    [Fact]
    public async Task Get_PerformancesToday_LabeledToday()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var film = await AddFilmAsync(db, "Perfect Days");
            var room = await AddRoomAsync(db, film.CinemaId, "Saal 2");
            db.Performances.Add(NewPerformance(film, room.Id, daysFromToday: 0, hour: 20));
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<ScheduleDto>("/api/schedule");

        Assert.NotNull(dto);
        var day = Assert.Single(dto.Days);
        Assert.Equal("Today", day.Label);
        var performance = Assert.Single(day.Performances);
        Assert.Equal("Perfect Days", performance.Title);
        Assert.Equal("Saal 2", performance.Room);
        Assert.Equal("20:00", performance.DateTime);
    }

    [Fact]
    public async Task Get_PerformanceTomorrow_LabeledTomorrow()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var film = await AddFilmAsync(db, "Past Lives");
            db.Performances.Add(NewPerformance(film, roomId: null, daysFromToday: 1, hour: 18));
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<ScheduleDto>("/api/schedule");

        Assert.NotNull(dto);
        var day = Assert.Single(dto.Days);
        Assert.Equal("Tomorrow", day.Label);
    }

    [Fact]
    public async Task Get_PerformanceFurtherOut_UsesDatedHeading()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        DateOnly targetDate;
        await using (var db = _factory.CreateDbContext())
        {
            var film = await AddFilmAsync(db, "Anatomy of a Fall");
            var performance = NewPerformance(film, roomId: null, daysFromToday: 5, hour: 21);
            targetDate = DateOnly.FromDateTime(CinemaTimeZone.ToLocal(performance.StartsAt).DateTime);
            db.Performances.Add(performance);
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<ScheduleDto>("/api/schedule");

        Assert.NotNull(dto);
        var day = Assert.Single(dto.Days);
        Assert.Equal(targetDate.ToString("ddd, MMM d", System.Globalization.CultureInfo.InvariantCulture), day.Label);
    }

    [Fact]
    public async Task Get_DaysWithNoPerformances_AreOmittedEntirely()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var film = await AddFilmAsync(db, "Nosferatu");
            db.Performances.AddRange(
                NewPerformance(film, roomId: null, daysFromToday: 0, hour: 20),
                NewPerformance(film, roomId: null, daysFromToday: 6, hour: 20));
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<ScheduleDto>("/api/schedule");

        Assert.NotNull(dto);
        // Only the two days that actually have a performance — none of the five empty days in between.
        Assert.Equal(2, dto.Days.Count);
    }

    [Fact]
    public async Task Get_PerformanceOutsideTheFourteenDayWindow_IsExcluded()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var film = await AddFilmAsync(db, "The Zone of Interest");
            db.Performances.Add(NewPerformance(film, roomId: null, daysFromToday: 14, hour: 20));
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<ScheduleDto>("/api/schedule");

        Assert.NotNull(dto);
        Assert.Empty(dto.Days);
    }

    [Fact]
    public async Task Get_WeekOffset_PagesTheWindowForwardBySevenDays()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var film = await AddFilmAsync(db, "Poor Things");
            db.Performances.AddRange(
                // Windows overlap (offset 0 spans days 0-13, offset 1 spans days 7-20), so these need
                // to sit outside each other's range to prove paging actually shifts the window rather
                // than just proving both windows can see an overlapping day.
                NewPerformance(film, roomId: null, daysFromToday: 2, hour: 20), // inside offset 0 only (< day 7)
                NewPerformance(film, roomId: null, daysFromToday: 15, hour: 20)); // inside offset 1 only (> day 13)
            await db.SaveChangesAsync();
        }

        var thisWeek = await client.GetFromJsonAsync<ScheduleDto>("/api/schedule?weekOffset=0");
        var nextWeek = await client.GetFromJsonAsync<ScheduleDto>("/api/schedule?weekOffset=1");

        Assert.NotNull(thisWeek);
        Assert.NotNull(nextWeek);
        Assert.Single(thisWeek.Days);
        Assert.Single(nextWeek.Days);
        Assert.Equal(1, nextWeek.WeekOffset);
    }

    [Fact]
    public async Task Get_NegativeWeekOffset_ClampsToZero()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var dto = await client.GetFromJsonAsync<ScheduleDto>("/api/schedule?weekOffset=-5");

        Assert.NotNull(dto);
        Assert.Equal(0, dto.WeekOffset);
    }

    [Fact]
    public async Task Get_TrackedFilmPerformance_MarksCardAsTracking()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var tracked = await AddFilmAsync(db, "Tracked Film");
            var untracked = await AddFilmAsync(db, "Untracked Film");
            db.TrackedMovies.Add(new TrackedMovie { FilmId = tracked.Id, CreatedAt = DateTimeOffset.UtcNow });
            db.Performances.AddRange(
                NewPerformance(tracked, roomId: null, daysFromToday: 0, hour: 12),
                NewPerformance(untracked, roomId: null, daysFromToday: 0, hour: 14));
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<ScheduleDto>("/api/schedule");

        Assert.NotNull(dto);
        var day = Assert.Single(dto.Days);
        var trackedCard = Assert.Single(day.Performances, p => p.Title == "Tracked Film");
        var untrackedCard = Assert.Single(day.Performances, p => p.Title == "Untracked Film");
        Assert.True(trackedCard.Tracking);
        Assert.False(untrackedCard.Tracking);
    }

    [Fact]
    public async Task Get_CancelledPerformance_ShowsCancelledBadge()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var film = await AddFilmAsync(db, "Cancelled Screening");
            var performance = NewPerformance(film, roomId: null, daysFromToday: 0, hour: 20);
            performance.Status = PerformanceStatus.Cancelled;
            db.Performances.Add(performance);
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<ScheduleDto>("/api/schedule");

        Assert.NotNull(dto);
        var day = Assert.Single(dto.Days);
        var card = Assert.Single(day.Performances);
        Assert.Equal(PerformanceCardStatus.Cancelled, card.Status);
    }

    private static Performance NewPerformance(Film film, int? roomId, int daysFromToday, int hour)
    {
        var todayLocal = DateOnly.FromDateTime(CinemaTimeZone.ToLocal(DateTimeOffset.UtcNow).DateTime);
        var localDateTime = todayLocal.AddDays(daysFromToday).ToDateTime(new TimeOnly(hour, 0));
        // Npgsql only accepts offset-zero DateTimeOffset for timestamptz — normalize the Berlin-offset
        // instant to UTC before it's persisted, same as HomeEndpointsTests/PerformanceDetailEndpointsTests'
        // own NextFriday helper does.
        var startsAt = new DateTimeOffset(localDateTime, CinemaTimeZone.Berlin.GetUtcOffset(localDateTime)).ToUniversalTime();

        return new Performance
        {
            FilmId = film.Id,
            CinemaId = film.CinemaId,
            RoomId = roomId,
            SourcePerformanceId = Guid.NewGuid().ToString(),
            StartsAt = startsAt,
            BookingLink = "https://example.invalid/book",
            Status = PerformanceStatus.Normal,
            IsSoldOut = false,
            IsBookable = true,
            LastSeenAt = DateTimeOffset.UtcNow,
        };
    }

    private static async Task<Room> AddRoomAsync(CineScoutDbContext db, int cinemaId, string name)
    {
        var room = new Room { CinemaId = cinemaId, ExternalAuditoriumId = Guid.NewGuid().ToString(), Name = name };
        db.Rooms.Add(room);
        await db.SaveChangesAsync();
        return room;
    }

    private static async Task<Film> AddFilmAsync(CineScoutDbContext db, string title)
    {
        var cinema = new Cinema { Name = $"Cinema for {title}", ExternalCinemaId = Guid.NewGuid().ToString(), CrawlBaseUrl = "https://example.invalid", IsActive = true };
        db.Cinemas.Add(cinema);
        await db.SaveChangesAsync();

        var film = new Film { CinemaId = cinema.Id, ExternalFilmId = Guid.NewGuid().ToString(), Title = title };
        db.Films.Add(film);
        await db.SaveChangesAsync();
        return film;
    }
}

public sealed class ScheduleEndpointsTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
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

    /// <summary>Tests share one Postgres container/schema; clear out everything Schedule's read model touches between tests.</summary>
    public async Task ResetDatabaseAsync()
    {
        await using var db = CreateDbContext();
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
