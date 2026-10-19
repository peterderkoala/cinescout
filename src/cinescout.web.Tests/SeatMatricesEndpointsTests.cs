using System.Net;
using System.Net.Http.Json;
using cinescout.contracts;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Tests;

public sealed class SeatMatricesEndpointsTests : IClassFixture<PostgresWebFactory>
{
    private readonly PostgresWebFactory _factory;

    public SeatMatricesEndpointsTests(PostgresWebFactory factory) => _factory = factory;

    private static SeatMatrixWriteRequest GeneralRequest(int roomId) => new()
    {
        RoomId = roomId,
        FilmId = null,
        Name = "Sweet spot",
        RowStart = "D",
        RowEnd = "F",
        SeatNumberStart = 4,
        SeatNumberEnd = 9,
        PartySize = 2,
    };

    private static SeatMatrixWriteRequest OverrideRequest(int roomId, int filmId) => new()
    {
        RoomId = roomId,
        FilmId = filmId,
        Name = "Premiere seats",
        RowStart = "A",
        RowEnd = "C",
        SeatNumberStart = 5,
        SeatNumberEnd = 8,
        PartySize = 3,
    };

    [Fact]
    public async Task GetPage_ReturnsCinemasRoomsFilmsAndMatrices()
    {
        await _factory.ResetDatabaseAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var (cinemaId, roomId, filmId) = await SeedCinemaRoomAndFilmAsync();
        var createClient = await _factory.CreateWritableClientAsync();
        var general = await (await createClient.PostAsJsonAsync("/api/seat-matrices", GeneralRequest(roomId))).Content.ReadFromJsonAsync<SeatMatrixDto>();
        Assert.NotNull(general);

        var page = await client.GetFromJsonAsync<SeatMatricesPageDto>("/api/seat-matrices");

        Assert.NotNull(page);
        Assert.Contains(page.Cinemas, c => c.Id == cinemaId);
        Assert.Contains(page.Rooms, r => r.Id == roomId && r.CinemaId == cinemaId);
        Assert.Contains(page.Films, f => f.Id == filmId);
        Assert.Contains(page.Matrices, m => m.Id == general.Id && m.FilmId == null);
    }

    [Fact]
    public async Task Post_CreatesGeneralMatrix_AndReturns201WithLocation()
    {
        await _factory.ResetDatabaseAsync();
        var (_, roomId, _) = await SeedCinemaRoomAndFilmAsync();
        var client = await _factory.CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/seat-matrices", GeneralRequest(roomId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var dto = await response.Content.ReadFromJsonAsync<SeatMatrixDto>();
        Assert.NotNull(dto);
        Assert.Equal(roomId, dto.RoomId);
        Assert.Null(dto.FilmId);
        Assert.True(dto.IsEnabled);
    }

    [Fact]
    public async Task Post_CreatesFilmOverride_AndReturns201()
    {
        await _factory.ResetDatabaseAsync();
        var (_, roomId, filmId) = await SeedCinemaRoomAndFilmAsync();
        var client = await _factory.CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/seat-matrices", OverrideRequest(roomId, filmId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<SeatMatrixDto>();
        Assert.NotNull(dto);
        Assert.Equal(filmId, dto.FilmId);
    }

    [Fact]
    public async Task Post_SecondGeneralMatrixForSameRoom_ReturnsProblemDetails409()
    {
        await _factory.ResetDatabaseAsync();
        var (_, roomId, _) = await SeedCinemaRoomAndFilmAsync();
        var client = await _factory.CreateWritableClientAsync();

        var first = await client.PostAsJsonAsync("/api/seat-matrices", GeneralRequest(roomId));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync("/api/seat-matrices", GeneralRequest(roomId) with { Name = "Another" });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Post_MissingName_ReturnsProblemDetails400()
    {
        await _factory.ResetDatabaseAsync();
        var (_, roomId, _) = await SeedCinemaRoomAndFilmAsync();
        var client = await _factory.CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/seat-matrices", GeneralRequest(roomId) with { Name = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Name is required", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_UnknownRoom_ReturnsProblemDetails400()
    {
        var client = await _factory.CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/seat-matrices", GeneralRequest(999999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Room not found", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_UnknownFilm_ReturnsProblemDetails400()
    {
        await _factory.ResetDatabaseAsync();
        var (_, roomId, _) = await SeedCinemaRoomAndFilmAsync();
        var client = await _factory.CreateWritableClientAsync();

        var response = await client.PostAsJsonAsync("/api/seat-matrices", OverrideRequest(roomId, 999999));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Film not found", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_WithoutAntiforgeryToken_Returns400()
    {
        await _factory.ResetDatabaseAsync();
        var (_, roomId, _) = await SeedCinemaRoomAndFilmAsync();
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.PostAsJsonAsync("/api/seat-matrices", GeneralRequest(roomId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UpdatesExistingMatrix_PreservesIsEnabled()
    {
        await _factory.ResetDatabaseAsync();
        var (_, roomId, _) = await SeedCinemaRoomAndFilmAsync();
        var client = await _factory.CreateWritableClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/seat-matrices", GeneralRequest(roomId))).Content.ReadFromJsonAsync<SeatMatrixDto>();
        Assert.NotNull(created);

        var toggled = await (await client.PostAsync($"/api/seat-matrices/{created.Id}/toggle", content: null)).Content.ReadFromJsonAsync<SeatMatrixDto>();
        Assert.NotNull(toggled);
        Assert.False(toggled.IsEnabled);

        var response = await client.PutAsJsonAsync($"/api/seat-matrices/{created.Id}", GeneralRequest(roomId) with { Name = "Renamed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<SeatMatrixDto>();
        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated.Name);
        Assert.False(updated.IsEnabled);
    }

    [Fact]
    public async Task Put_UnknownId_Returns404()
    {
        await _factory.ResetDatabaseAsync();
        var (_, roomId, _) = await SeedCinemaRoomAndFilmAsync();
        var client = await _factory.CreateWritableClientAsync();

        var response = await client.PutAsJsonAsync("/api/seat-matrices/999999", GeneralRequest(roomId));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesMatrix_AndIsIdempotent()
    {
        await _factory.ResetDatabaseAsync();
        var (_, roomId, _) = await SeedCinemaRoomAndFilmAsync();
        var client = await _factory.CreateWritableClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/seat-matrices", GeneralRequest(roomId))).Content.ReadFromJsonAsync<SeatMatrixDto>();
        Assert.NotNull(created);

        var first = await client.DeleteAsync($"/api/seat-matrices/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await client.DeleteAsync($"/api/seat-matrices/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        await using var read = _factory.CreateDbContext();
        Assert.False(await read.FavoriteSeatMatrices.AnyAsync(m => m.Id == created.Id));
    }

    [Fact]
    public async Task Toggle_FlipsIsEnabled()
    {
        await _factory.ResetDatabaseAsync();
        var (_, roomId, _) = await SeedCinemaRoomAndFilmAsync();
        var client = await _factory.CreateWritableClientAsync();

        var created = await (await client.PostAsJsonAsync("/api/seat-matrices", GeneralRequest(roomId))).Content.ReadFromJsonAsync<SeatMatrixDto>();
        Assert.NotNull(created);
        Assert.True(created.IsEnabled);

        var toggled = await (await client.PostAsync($"/api/seat-matrices/{created.Id}/toggle", content: null)).Content.ReadFromJsonAsync<SeatMatrixDto>();
        Assert.NotNull(toggled);
        Assert.False(toggled.IsEnabled);

        var toggledAgain = await (await client.PostAsync($"/api/seat-matrices/{created.Id}/toggle", content: null)).Content.ReadFromJsonAsync<SeatMatrixDto>();
        Assert.NotNull(toggledAgain);
        Assert.True(toggledAgain.IsEnabled);
    }

    [Fact]
    public async Task Toggle_UnknownId_Returns404()
    {
        var client = await _factory.CreateWritableClientAsync();

        var response = await client.PostAsync("/api/seat-matrices/999999/toggle", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(int CinemaId, int RoomId, int FilmId)> SeedCinemaRoomAndFilmAsync()
    {
        await using var db = _factory.CreateDbContext();

        var cinema = new Cinema
        {
            Name = "HALL OF FAME - Kino in Kamp-Lintfort",
            ExternalCinemaId = "580",
            CrawlBaseUrl = "https://kamp-lintfort.hall-of-fame.website",
            IsActive = true,
        };
        db.Cinemas.Add(cinema);
        await db.SaveChangesAsync();

        var room = new Room { CinemaId = cinema.Id, ExternalAuditoriumId = "8255", Name = "Kino 1" };
        db.Rooms.Add(room);

        var film = new Film { CinemaId = cinema.Id, ExternalFilmId = "401865", Title = "Vaiana - Live Action" };
        db.Films.Add(film);
        await db.SaveChangesAsync();

        return (cinema.Id, room.Id, film.Id);
    }
}
