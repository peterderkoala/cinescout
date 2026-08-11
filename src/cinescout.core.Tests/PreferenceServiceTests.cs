using cinescout.core.Preferences;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace cinescout.core.Tests;

public class PreferenceServiceTests : IAsyncLifetime
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

    private async Task<(int RoomId, int FilmId)> SeedRoomAndFilmAsync()
    {
        var options = BuildOptions();
        await using var db = new CineScoutDbContext(options);

        var cinema = new Cinema
        {
            ExternalCinemaId = "580",
            Name = "HALL OF FAME - Kino in Kamp-Lintfort",
            CrawlBaseUrl = "https://kamp-lintfort.hall-of-fame.website",
            IsActive = true,
        };
        db.Cinemas.Add(cinema);
        await db.SaveChangesAsync();

        var room = new Room
        {
            CinemaId = cinema.Id,
            ExternalAuditoriumId = "8255",
            Name = "Kino 1",
        };
        db.Rooms.Add(room);

        var film = new Film
        {
            CinemaId = cinema.Id,
            ExternalFilmId = "401865",
            Title = "Vaiana - Live Action",
        };
        db.Films.Add(film);
        await db.SaveChangesAsync();

        return (room.Id, film.Id);
    }

    [Fact]
    public async Task Creates_updates_and_deletes_a_time_window()
    {
        var options = BuildOptions();

        int windowId;
        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            windowId = await service.CreateTimeWindowAsync(
                DaysOfWeekFlags.Friday | DaysOfWeekFlags.Saturday,
                new TimeOnly(19, 0),
                new TimeOnly(22, 30),
                CancellationToken.None);
        }

        await using (var read = new CineScoutDbContext(options))
        {
            var window = await read.FavoriteTimeWindows.SingleAsync(w => w.Id == windowId);
            Assert.Equal(DaysOfWeekFlags.Friday | DaysOfWeekFlags.Saturday, window.DaysOfWeek);
            Assert.Equal(new TimeOnly(19, 0), window.StartTime);
            Assert.Equal(new TimeOnly(22, 30), window.EndTime);
        }

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            await service.UpdateTimeWindowAsync(
                windowId,
                DaysOfWeekFlags.Sunday,
                new TimeOnly(15, 0),
                new TimeOnly(18, 0),
                CancellationToken.None);
        }

        await using (var read = new CineScoutDbContext(options))
        {
            var window = await read.FavoriteTimeWindows.SingleAsync(w => w.Id == windowId);
            Assert.Equal(DaysOfWeekFlags.Sunday, window.DaysOfWeek);
            Assert.Equal(new TimeOnly(15, 0), window.StartTime);
            Assert.Equal(new TimeOnly(18, 0), window.EndTime);
        }

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            await service.DeleteTimeWindowAsync(windowId, CancellationToken.None);
        }

        await using (var read = new CineScoutDbContext(options))
        {
            Assert.Empty(await read.FavoriteTimeWindows.ToListAsync());
        }
    }

    [Fact]
    public async Task Update_and_delete_of_missing_time_window_are_no_ops()
    {
        var options = BuildOptions();

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            await service.UpdateTimeWindowAsync(12345, DaysOfWeekFlags.Monday, new TimeOnly(10, 0), new TimeOnly(12, 0), CancellationToken.None);
            await service.DeleteTimeWindowAsync(12345, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.Empty(await read.FavoriteTimeWindows.ToListAsync());
    }

    [Fact]
    public async Task Creates_updates_and_deletes_a_general_seat_matrix()
    {
        var options = BuildOptions();
        var (roomId, _) = await SeedRoomAndFilmAsync();

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            await service.CreateSeatMatrixAsync(
                new SeatMatrixInput(roomId, FilmId: null, "Sweet spot", "D", "F", 4, 9, 2, IsEnabled: true),
                CancellationToken.None);
        }

        int matrixId;
        await using (var read = new CineScoutDbContext(options))
        {
            var matrix = await read.FavoriteSeatMatrices.SingleAsync();
            matrixId = matrix.Id;
            Assert.Equal(roomId, matrix.RoomId);
            Assert.Null(matrix.FilmId);
            Assert.Equal("Sweet spot", matrix.Name);
            Assert.Equal("D", matrix.RowStart);
            Assert.Equal("F", matrix.RowEnd);
            Assert.Equal(4, matrix.SeatNumberStart);
            Assert.Equal(9, matrix.SeatNumberEnd);
            Assert.Equal(2, matrix.PartySize);
            Assert.True(matrix.IsEnabled);
        }

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            await service.UpdateSeatMatrixAsync(
                matrixId,
                new SeatMatrixInput(roomId, FilmId: null, "Back rows", "G", "H", 1, 12, 4, IsEnabled: false),
                CancellationToken.None);
        }

        await using (var read = new CineScoutDbContext(options))
        {
            var matrix = await read.FavoriteSeatMatrices.SingleAsync(m => m.Id == matrixId);
            Assert.Equal("Back rows", matrix.Name);
            Assert.Equal("G", matrix.RowStart);
            Assert.Equal("H", matrix.RowEnd);
            Assert.Equal(1, matrix.SeatNumberStart);
            Assert.Equal(12, matrix.SeatNumberEnd);
            Assert.Equal(4, matrix.PartySize);
            Assert.False(matrix.IsEnabled);
        }

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            await service.DeleteSeatMatrixAsync(matrixId, CancellationToken.None);
        }

        await using (var read = new CineScoutDbContext(options))
        {
            Assert.Empty(await read.FavoriteSeatMatrices.ToListAsync());
        }
    }

    [Fact]
    public async Task Creates_a_film_specific_seat_matrix()
    {
        var options = BuildOptions();
        var (roomId, filmId) = await SeedRoomAndFilmAsync();

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            await service.CreateSeatMatrixAsync(
                new SeatMatrixInput(roomId, filmId, "Vaiana premiere seats", "A", "C", 5, 8, 3, IsEnabled: true),
                CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var matrix = await read.FavoriteSeatMatrices.SingleAsync();
        Assert.Equal(roomId, matrix.RoomId);
        Assert.Equal(filmId, matrix.FilmId);
        Assert.Equal("Vaiana premiere seats", matrix.Name);
    }

    [Fact]
    public async Task Update_and_delete_of_missing_seat_matrix_are_no_ops()
    {
        var options = BuildOptions();
        var (roomId, _) = await SeedRoomAndFilmAsync();

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            await service.UpdateSeatMatrixAsync(
                12345,
                new SeatMatrixInput(roomId, FilmId: null, "Ghost", "A", "B", 1, 2, 1, IsEnabled: true),
                CancellationToken.None);
            await service.DeleteSeatMatrixAsync(12345, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.Empty(await read.FavoriteSeatMatrices.ToListAsync());
    }

    [Fact]
    public async Task SetSeatMatrixEnabled_flips_only_IsEnabled_and_preserves_the_row()
    {
        var options = BuildOptions();
        var (roomId, _) = await SeedRoomAndFilmAsync();

        int matrixId;
        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            await service.CreateSeatMatrixAsync(
                new SeatMatrixInput(roomId, FilmId: null, "Sweet spot", "D", "F", 4, 9, 2, IsEnabled: true),
                CancellationToken.None);
            matrixId = (await db.FavoriteSeatMatrices.SingleAsync()).Id;
        }

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            await service.SetSeatMatrixEnabledAsync(matrixId, enabled: false, CancellationToken.None);
        }

        await using (var read = new CineScoutDbContext(options))
        {
            var matrix = await read.FavoriteSeatMatrices.SingleAsync(m => m.Id == matrixId);
            Assert.False(matrix.IsEnabled);
            Assert.Equal(roomId, matrix.RoomId);
            Assert.Null(matrix.FilmId);
            Assert.Equal("Sweet spot", matrix.Name);
            Assert.Equal("D", matrix.RowStart);
            Assert.Equal("F", matrix.RowEnd);
            Assert.Equal(4, matrix.SeatNumberStart);
            Assert.Equal(9, matrix.SeatNumberEnd);
            Assert.Equal(2, matrix.PartySize);
        }

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            await service.SetSeatMatrixEnabledAsync(matrixId, enabled: true, CancellationToken.None);
        }

        await using (var read = new CineScoutDbContext(options))
        {
            var matrix = await read.FavoriteSeatMatrices.SingleAsync(m => m.Id == matrixId);
            Assert.True(matrix.IsEnabled);
        }
    }

    [Fact]
    public async Task SetSeatMatrixEnabled_on_missing_id_is_a_no_op()
    {
        var options = BuildOptions();

        await using (var db = new CineScoutDbContext(options))
        {
            var service = new PreferenceService(db);
            await service.SetSeatMatrixEnabledAsync(12345, enabled: true, CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.Empty(await read.FavoriteSeatMatrices.ToListAsync());
    }
}
