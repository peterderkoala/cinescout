using cinescout.core.Cinemas;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace cinescout.core.Tests;

public class CinemaServiceTests : IAsyncLifetime
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

    private static readonly CinemaInput ExampleInput = new(
        Name: "HALL OF FAME - Kino in Kamp-Lintfort",
        ExternalCinemaId: "580",
        CrawlBaseUrl: "https://kamp-lintfort.hall-of-fame.website",
        IsActive: true);

    private static async Task<int> CreateCinemaAsync(CinemaService service, CinemaInput input)
    {
        var (outcome, id) = await service.CreateAsync(input, CancellationToken.None);
        Assert.Equal(CinemaSaveOutcome.Saved, outcome);
        return id;
    }

    [Fact]
    public async Task CreateAsync_persists_a_new_cinema()
    {
        var options = BuildOptions();

        int cinemaId;
        await using (var db = new CineScoutDbContext(options))
        {
            var service = new CinemaService(db);
            cinemaId = await CreateCinemaAsync(service, ExampleInput);
        }

        await using var read = new CineScoutDbContext(options);
        var cinema = await read.Cinemas.SingleAsync(c => c.Id == cinemaId);
        Assert.Equal("HALL OF FAME - Kino in Kamp-Lintfort", cinema.Name);
        Assert.Equal("580", cinema.ExternalCinemaId);
        Assert.Equal("https://kamp-lintfort.hall-of-fame.website", cinema.CrawlBaseUrl);
        Assert.True(cinema.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_overwrites_all_editable_fields()
    {
        var options = BuildOptions();

        int cinemaId;
        await using (var db = new CineScoutDbContext(options))
        {
            var service = new CinemaService(db);
            cinemaId = await CreateCinemaAsync(service, ExampleInput);

            await service.UpdateAsync(
                cinemaId,
                new CinemaInput("Renamed Cinema", "581", "https://renamed.invalid", IsActive: false),
                CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var cinema = await read.Cinemas.SingleAsync(c => c.Id == cinemaId);
        Assert.Equal("Renamed Cinema", cinema.Name);
        Assert.Equal("581", cinema.ExternalCinemaId);
        Assert.Equal("https://renamed.invalid", cinema.CrawlBaseUrl);
        Assert.False(cinema.IsActive);
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_ExternalCinemaId()
    {
        var options = BuildOptions();

        await using var db = new CineScoutDbContext(options);
        var service = new CinemaService(db);
        await CreateCinemaAsync(service, ExampleInput);

        var (outcome, id) = await service.CreateAsync(ExampleInput with { Name = "A Different Name" }, CancellationToken.None);

        Assert.Equal(CinemaSaveOutcome.DuplicateExternalCinemaId, outcome);
        Assert.Equal(1, await db.Cinemas.CountAsync());
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_duplicate_ExternalCinemaId_from_another_cinema()
    {
        var options = BuildOptions();

        await using var db = new CineScoutDbContext(options);
        var service = new CinemaService(db);
        await CreateCinemaAsync(service, ExampleInput);
        var secondId = await CreateCinemaAsync(service, ExampleInput with { ExternalCinemaId = "581" });

        var outcome = await service.UpdateAsync(secondId, ExampleInput with { Name = "Renamed" }, CancellationToken.None);

        Assert.Equal(CinemaSaveOutcome.DuplicateExternalCinemaId, outcome);
        var untouched = await db.Cinemas.SingleAsync(c => c.Id == secondId);
        Assert.Equal("581", untouched.ExternalCinemaId);
        Assert.NotEqual("Renamed", untouched.Name);
    }

    [Fact]
    public async Task UpdateAsync_allows_keeping_its_own_ExternalCinemaId()
    {
        var options = BuildOptions();

        await using var db = new CineScoutDbContext(options);
        var service = new CinemaService(db);
        var id = await CreateCinemaAsync(service, ExampleInput);

        var outcome = await service.UpdateAsync(id, ExampleInput with { Name = "Renamed" }, CancellationToken.None);

        Assert.Equal(CinemaSaveOutcome.Saved, outcome);
        var updated = await db.Cinemas.SingleAsync(c => c.Id == id);
        Assert.Equal("Renamed", updated.Name);
    }

    [Fact]
    public async Task UpdateAsync_on_missing_id_is_a_defensive_no_op()
    {
        var options = BuildOptions();

        await using var db = new CineScoutDbContext(options);
        var service = new CinemaService(db);

        await service.UpdateAsync(999, ExampleInput, CancellationToken.None);

        Assert.False(await db.Cinemas.AnyAsync());
    }

    [Fact]
    public async Task DeleteAsync_removes_a_cinema_with_no_rooms()
    {
        var options = BuildOptions();

        int cinemaId;
        await using (var db = new CineScoutDbContext(options))
        {
            var service = new CinemaService(db);
            cinemaId = await CreateCinemaAsync(service, ExampleInput);

            var outcome = await service.DeleteAsync(cinemaId, CancellationToken.None);
            Assert.Equal(CinemaDeleteOutcome.Deleted, outcome);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.False(await read.Cinemas.AnyAsync(c => c.Id == cinemaId));
    }

    [Fact]
    public async Task DeleteAsync_on_missing_id_is_a_defensive_no_op()
    {
        var options = BuildOptions();

        await using var db = new CineScoutDbContext(options);
        var service = new CinemaService(db);

        var outcome = await service.DeleteAsync(999, CancellationToken.None);

        Assert.Equal(CinemaDeleteOutcome.Deleted, outcome);
    }

    [Fact]
    public async Task DeleteAsync_is_blocked_when_the_cinema_has_a_room()
    {
        var options = BuildOptions();

        int cinemaId;
        await using (var db = new CineScoutDbContext(options))
        {
            var service = new CinemaService(db);
            cinemaId = await CreateCinemaAsync(service, ExampleInput);

            db.Rooms.Add(new Room { CinemaId = cinemaId, ExternalAuditoriumId = "8255", Name = "Kino 1" });
            await db.SaveChangesAsync();

            var outcome = await service.DeleteAsync(cinemaId, CancellationToken.None);
            Assert.Equal(CinemaDeleteOutcome.BlockedHasRooms, outcome);
        }

        await using var read = new CineScoutDbContext(options);
        Assert.True(await read.Cinemas.AnyAsync(c => c.Id == cinemaId));
    }

    [Fact]
    public async Task RenameRoomAsync_updates_only_the_room_name()
    {
        var options = BuildOptions();

        int roomId;
        await using (var db = new CineScoutDbContext(options))
        {
            var service = new CinemaService(db);
            var cinemaId = await CreateCinemaAsync(service, ExampleInput);

            var room = new Room { CinemaId = cinemaId, ExternalAuditoriumId = "8255", Name = "Kino 1" };
            db.Rooms.Add(room);
            await db.SaveChangesAsync();
            roomId = room.Id;

            await service.RenameRoomAsync(roomId, "Kino 1 (Renamed)", CancellationToken.None);
        }

        await using var read = new CineScoutDbContext(options);
        var room2 = await read.Rooms.SingleAsync(r => r.Id == roomId);
        Assert.Equal("Kino 1 (Renamed)", room2.Name);
        Assert.Equal("8255", room2.ExternalAuditoriumId);
    }

    [Fact]
    public async Task RenameRoomAsync_on_missing_id_is_a_defensive_no_op()
    {
        var options = BuildOptions();

        await using var db = new CineScoutDbContext(options);
        var service = new CinemaService(db);

        await service.RenameRoomAsync(999, "Doesn't matter", CancellationToken.None);
    }
}
