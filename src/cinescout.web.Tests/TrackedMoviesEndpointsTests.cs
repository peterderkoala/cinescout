using System.Net;
using System.Net.Http.Json;
using cinescout.contracts;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Tests;

public sealed class TrackedMoviesEndpointsTests : IClassFixture<PostgresWebFactory>
{
    private readonly PostgresWebFactory _factory;

    public TrackedMoviesEndpointsTests(PostgresWebFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_NoTrackedMoviesAndNoFilms_ReturnsEmptyLists()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var dto = await client.GetFromJsonAsync<TrackedMoviesPageDto>("/api/tracked-movies");

        Assert.NotNull(dto);
        Assert.Empty(dto.TrackedMovies);
        Assert.Empty(dto.AllFilms);
    }

    [Fact]
    public async Task Get_TrackedFilmWithUpcomingPerformances_ReturnsCompactCards()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var cinema = await AddCinemaAsync(db, "Kino am Rathaus");
            var room = await AddRoomAsync(db, cinema.Id, "Saal 2");
            var film = await AddFilmAsync(db, "Perfect Days", cinema.Id);
            db.TrackedMovies.Add(new TrackedMovie { FilmId = film.Id, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            db.Performances.AddRange(
                new Performance
                {
                    FilmId = film.Id,
                    CinemaId = cinema.Id,
                    RoomId = room.Id,
                    SourcePerformanceId = "1",
                    StartsAt = DateTimeOffset.UtcNow.AddDays(1),
                    BookingLink = "https://example.invalid/book/1",
                    Status = PerformanceStatus.Normal,
                    IsSoldOut = false,
                    IsBookable = true,
                    LastSeenAt = DateTimeOffset.UtcNow,
                },
                new Performance
                {
                    FilmId = film.Id,
                    CinemaId = cinema.Id,
                    RoomId = room.Id,
                    SourcePerformanceId = "2",
                    StartsAt = DateTimeOffset.UtcNow.AddDays(-1),
                    BookingLink = "https://example.invalid/book/2",
                    Status = PerformanceStatus.Normal,
                    IsSoldOut = false,
                    IsBookable = true,
                    LastSeenAt = DateTimeOffset.UtcNow,
                },
                new Performance
                {
                    FilmId = film.Id,
                    CinemaId = cinema.Id,
                    RoomId = room.Id,
                    SourcePerformanceId = "3",
                    StartsAt = DateTimeOffset.UtcNow.AddDays(2),
                    BookingLink = "https://example.invalid/book/3",
                    Status = PerformanceStatus.Cancelled,
                    IsSoldOut = false,
                    IsBookable = true,
                    LastSeenAt = DateTimeOffset.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<TrackedMoviesPageDto>("/api/tracked-movies");

        Assert.NotNull(dto);
        var row = Assert.Single(dto.TrackedMovies);
        Assert.Equal("Perfect Days", row.Title);
        // Only the future, non-cancelled performance — the past one and the cancelled one are excluded.
        var performance = Assert.Single(row.Performances);
        Assert.Equal("Saal 2", performance.Room);
        Assert.True(performance.Tracking);
        Assert.Empty(dto.AllFilms);
    }

    [Fact]
    public async Task Get_TrackedFilmWithNoUpcomingPerformances_ReturnsEmptyPerformanceList()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var film = await AddFilmAsync(db, "Past Lives");
            db.TrackedMovies.Add(new TrackedMovie { FilmId = film.Id, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }

        var dto = await client.GetFromJsonAsync<TrackedMoviesPageDto>("/api/tracked-movies");

        Assert.NotNull(dto);
        var row = Assert.Single(dto.TrackedMovies);
        Assert.Equal("Past Lives", row.Title);
        Assert.Empty(row.Performances);
    }

    [Fact]
    public async Task Get_AllFilms_OnlyListsUntrackedFilmsWithUpcomingPerformances()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        await using (var db = _factory.CreateDbContext())
        {
            var eligible = await AddFilmAsync(db, "Nosferatu");
            var tracked = await AddFilmAsync(db, "Poor Things");
            var noUpcoming = await AddFilmAsync(db, "The Zone of Interest");

            db.TrackedMovies.Add(new TrackedMovie { FilmId = tracked.Id, CreatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();

            db.Performances.AddRange(
                new Performance
                {
                    FilmId = eligible.Id,
                    CinemaId = eligible.CinemaId,
                    RoomId = null,
                    SourcePerformanceId = "eligible-1",
                    StartsAt = DateTimeOffset.UtcNow.AddDays(1),
                    BookingLink = "https://example.invalid/book/eligible-1",
                    Status = PerformanceStatus.Normal,
                    IsSoldOut = false,
                    IsBookable = true,
                    LastSeenAt = DateTimeOffset.UtcNow,
                },
                new Performance
                {
                    FilmId = tracked.Id,
                    CinemaId = tracked.CinemaId,
                    RoomId = null,
                    SourcePerformanceId = "tracked-1",
                    StartsAt = DateTimeOffset.UtcNow.AddDays(1),
                    BookingLink = "https://example.invalid/book/tracked-1",
                    Status = PerformanceStatus.Normal,
                    IsSoldOut = false,
                    IsBookable = true,
                    LastSeenAt = DateTimeOffset.UtcNow,
                });
            await db.SaveChangesAsync();
            _ = noUpcoming;
        }

        var dto = await client.GetFromJsonAsync<TrackedMoviesPageDto>("/api/tracked-movies");

        Assert.NotNull(dto);
        var allFilmsRow = Assert.Single(dto.AllFilms);
        Assert.Equal("Nosferatu", allFilmsRow.Title);
    }

    [Fact]
    public async Task Track_MovesFilmFromAllFilmsToTrackedMovies()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateWritableClientAsync();

        int filmId;
        await using (var db = _factory.CreateDbContext())
        {
            var film = await AddFilmAsync(db, "Anatomy of a Fall");
            filmId = film.Id;

            db.Performances.Add(new Performance
            {
                FilmId = film.Id,
                CinemaId = film.CinemaId,
                RoomId = null,
                SourcePerformanceId = "track-1",
                StartsAt = DateTimeOffset.UtcNow.AddDays(1),
                BookingLink = "https://example.invalid/book/track-1",
                Status = PerformanceStatus.Normal,
                IsSoldOut = false,
                IsBookable = true,
                LastSeenAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/tracked-movies/{filmId}/track", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<TrackedMoviesPageDto>();
        Assert.NotNull(dto);
        Assert.Contains(dto.TrackedMovies, r => r.FilmId == filmId);
        Assert.DoesNotContain(dto.AllFilms, f => f.FilmId == filmId);

        await using var read = _factory.CreateDbContext();
        Assert.True(await read.TrackedMovies.AnyAsync(t => t.FilmId == filmId));
    }

    [Fact]
    public async Task Untrack_MovesFilmFromTrackedMoviesToAllFilms()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateWritableClientAsync();

        int filmId;
        await using (var db = _factory.CreateDbContext())
        {
            var film = await AddFilmAsync(db, "The Holdovers");
            filmId = film.Id;
            db.TrackedMovies.Add(new TrackedMovie { FilmId = filmId, CreatedAt = DateTimeOffset.UtcNow });

            db.Performances.Add(new Performance
            {
                FilmId = film.Id,
                CinemaId = film.CinemaId,
                RoomId = null,
                SourcePerformanceId = "untrack-1",
                StartsAt = DateTimeOffset.UtcNow.AddDays(1),
                BookingLink = "https://example.invalid/book/untrack-1",
                Status = PerformanceStatus.Normal,
                IsSoldOut = false,
                IsBookable = true,
                LastSeenAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/tracked-movies/{filmId}/untrack", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<TrackedMoviesPageDto>();
        Assert.NotNull(dto);
        Assert.DoesNotContain(dto.TrackedMovies, r => r.FilmId == filmId);
        Assert.Contains(dto.AllFilms, f => f.FilmId == filmId);

        await using var read = _factory.CreateDbContext();
        Assert.False(await read.TrackedMovies.AnyAsync(t => t.FilmId == filmId));
    }

    [Fact]
    public async Task Track_UnknownFilmId_Returns404()
    {
        var client = await _factory.CreateWritableClientAsync();

        var response = await client.PostAsync("/api/tracked-movies/999999/track", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Untrack_UnknownFilmId_Returns404()
    {
        var client = await _factory.CreateWritableClientAsync();

        var response = await client.PostAsync("/api/tracked-movies/999999/untrack", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
}
