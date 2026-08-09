using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace cinescout.persistence.Tests;

public class CineScoutDbContextTests : IAsyncLifetime
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

    [Fact]
    public async Task RoundTrips_one_instance_of_every_entity_type()
    {
        var options = BuildOptions();

        int cinemaId, filmId, roomId, performanceId, trackedMovieId, matchId;

        await using (var write = new CineScoutDbContext(options))
        {
            var cinema = new Cinema
            {
                ExternalCinemaId = "580",
                Name = "HALL OF FAME - Kino in Kamp-Lintfort",
                CrawlBaseUrl = "https://kamp-lintfort.hall-of-fame.website",
                IsActive = true,
            };
            write.Cinemas.Add(cinema);
            await write.SaveChangesAsync();
            cinemaId = cinema.Id;

            var film = new Film
            {
                CinemaId = cinemaId,
                ExternalFilmId = "401865",
                Title = "Vaiana - Live Action",
            };
            write.Films.Add(film);
            await write.SaveChangesAsync();
            filmId = film.Id;

            var room = new Room
            {
                CinemaId = cinemaId,
                ExternalAuditoriumId = "8259",
                Name = "Kino 3",
            };
            write.Rooms.Add(room);
            await write.SaveChangesAsync();
            roomId = room.Id;

            var performance = new Performance
            {
                FilmId = filmId,
                CinemaId = cinemaId,
                RoomId = roomId,
                SourcePerformanceId = "74705",
                StartsAt = new DateTimeOffset(2026, 7, 17, 19, 50, 0, TimeSpan.Zero),
                BookingLink = "https://www.kinoheld.de/kino-kamp-lintfort/hall-of-fame?mode=widget&change=no&showId=74705",
                Status = PerformanceStatus.Normal,
                IsSoldOut = false,
                IsBookable = true,
                LastSeenAt = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero),
            };
            write.Performances.Add(performance);
            await write.SaveChangesAsync();
            performanceId = performance.Id;

            write.PerformanceSnapshots.Add(new PerformanceSnapshot
            {
                PerformanceId = performanceId,
                CrawledAt = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero),
                RawPayload = """{"performanceID":74705,"status":"normal"}""",
            });

            write.SeatStatuses.Add(new SeatStatus
            {
                PerformanceId = performanceId,
                SourceSeatId = "21353011006",
                Row = "D",
                SeatNumber = 1,
                Status = SeatOccupancyStatus.Free,
                LeftNeighborSeatId = null,
                RightNeighborSeatId = "21353011007",
                UpdatedAt = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero),
            });

            var seatingSnapshot = new SeatingSnapshot
            {
                PerformanceId = performanceId,
                CrawledAt = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero),
                RawPayload = """{"seats":{"21353011006":{"status":"sf"}}}""",
            };
            write.SeatingSnapshots.Add(seatingSnapshot);
            await write.SaveChangesAsync();

            write.SeatingSnapshotSeats.Add(new SeatingSnapshotSeat
            {
                SnapshotId = seatingSnapshot.Id,
                SourceSeatId = "21353011006",
                Row = "D",
                SeatNumber = 1,
                Status = SeatOccupancyStatus.Free,
                LeftNeighborSeatId = null,
                RightNeighborSeatId = "21353011007",
            });

            var trackedMovie = new TrackedMovie
            {
                FilmId = filmId,
                CreatedAt = new DateTimeOffset(2026, 7, 17, 9, 0, 0, TimeSpan.Zero),
            };
            write.TrackedMovies.Add(trackedMovie);
            await write.SaveChangesAsync();
            trackedMovieId = trackedMovie.Id;

            write.FavoriteTimeWindows.Add(new FavoriteTimeWindow
            {
                DaysOfWeek = DaysOfWeekFlags.Saturday | DaysOfWeekFlags.Sunday,
                StartTime = new TimeOnly(14, 0),
                EndTime = new TimeOnly(18, 0),
            });

            write.FavoriteSeatMatrices.Add(new FavoriteSeatMatrix
            {
                RoomId = roomId,
                FilmId = null,
                Name = "General Kino 3 favorite",
                RowStart = "D",
                RowEnd = "G",
                SeatNumberStart = 4,
                SeatNumberEnd = 12,
                PartySize = 2,
                IsEnabled = true,
            });

            var match = new Match
            {
                PerformanceId = performanceId,
                TrackedMovieId = trackedMovieId,
                MatchedAt = new DateTimeOffset(2026, 7, 17, 12, 5, 0, TimeSpan.Zero),
                Status = MatchStatus.Active,
            };
            write.Matches.Add(match);
            await write.SaveChangesAsync();
            matchId = match.Id;

            write.NotificationLogs.Add(new NotificationLog
            {
                NotificationType = NotificationType.RulesMatched,
                MatchId = matchId,
                Channel = "Discord",
                SentAt = new DateTimeOffset(2026, 7, 17, 12, 5, 5, TimeSpan.Zero),
                Status = NotificationStatus.Success,
                ResponseDetail = "204 No Content",
            });

            await write.SaveChangesAsync();
        }

        // Read back through a fresh context (a new connection/no first-level cache) so this
        // verifies real persistence, not an in-memory identity map.
        await using var read = new CineScoutDbContext(options);

        var readCinema = await read.Cinemas.SingleAsync(s => s.Id == cinemaId);
        Assert.Equal("HALL OF FAME - Kino in Kamp-Lintfort", readCinema.Name);
        Assert.Equal("580", readCinema.ExternalCinemaId);
        Assert.True(readCinema.IsActive);

        var readFilm = await read.Films.SingleAsync(f => f.Id == filmId);
        Assert.Equal("Vaiana - Live Action", readFilm.Title);
        Assert.Equal(cinemaId, readFilm.CinemaId);

        var readRoom = await read.Rooms.SingleAsync(r => r.Id == roomId);
        Assert.Equal("Kino 3", readRoom.Name);
        Assert.Equal("8259", readRoom.ExternalAuditoriumId);

        var readPerformance = await read.Performances.SingleAsync(p => p.Id == performanceId);
        Assert.Equal("74705", readPerformance.SourcePerformanceId);
        Assert.Equal(roomId, readPerformance.RoomId);
        Assert.Equal(PerformanceStatus.Normal, readPerformance.Status);
        Assert.False(readPerformance.IsSoldOut);
        Assert.True(readPerformance.IsBookable);

        var readSnapshot = await read.PerformanceSnapshots.SingleAsync(s => s.PerformanceId == performanceId);
        Assert.Contains("74705", readSnapshot.RawPayload);

        var readSeat = await read.SeatStatuses.SingleAsync(s => s.PerformanceId == performanceId);
        Assert.Equal("D", readSeat.Row);
        Assert.Equal(1, readSeat.SeatNumber);
        Assert.Equal(SeatOccupancyStatus.Free, readSeat.Status);
        Assert.Null(readSeat.LeftNeighborSeatId);
        Assert.Equal("21353011007", readSeat.RightNeighborSeatId);

        var readSeatingSnapshot = await read.SeatingSnapshots.SingleAsync(s => s.PerformanceId == performanceId);
        var readSnapshotSeat = await read.SeatingSnapshotSeats.SingleAsync(s => s.SnapshotId == readSeatingSnapshot.Id);
        Assert.Equal("D", readSnapshotSeat.Row);
        Assert.Equal(SeatOccupancyStatus.Free, readSnapshotSeat.Status);

        var readTrackedMovie = await read.TrackedMovies.SingleAsync(w => w.Id == trackedMovieId);
        Assert.Equal(filmId, readTrackedMovie.FilmId);

        var readTimeWindow = await read.FavoriteTimeWindows.SingleAsync();
        Assert.Equal(DaysOfWeekFlags.Saturday | DaysOfWeekFlags.Sunday, readTimeWindow.DaysOfWeek);
        Assert.Equal(new TimeOnly(14, 0), readTimeWindow.StartTime);

        var readMatrix = await read.FavoriteSeatMatrices.SingleAsync();
        Assert.Equal(roomId, readMatrix.RoomId);
        Assert.Null(readMatrix.FilmId);
        Assert.Equal(2, readMatrix.PartySize);
        Assert.True(readMatrix.IsEnabled);

        var readMatch = await read.Matches.SingleAsync(m => m.Id == matchId);
        Assert.Equal(MatchStatus.Active, readMatch.Status);
        Assert.Equal(trackedMovieId, readMatch.TrackedMovieId);

        var readLog = await read.NotificationLogs.SingleAsync(n => n.MatchId == matchId);
        Assert.Equal(NotificationType.RulesMatched, readLog.NotificationType);
        Assert.Equal(NotificationStatus.Success, readLog.Status);
        Assert.Equal("Discord", readLog.Channel);
    }
}
