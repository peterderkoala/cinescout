using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.Preferences;

/// <summary>
/// Thin persistence layer for the user's preference entities (FavoriteTimeWindow,
/// FavoriteSeatMatrix). Validation is the UI's job; like WatchedMovieService, operations
/// on missing ids are defensive no-ops rather than errors.
/// </summary>
public sealed class PreferenceService(CineScoutDbContext db)
{
    public async Task CreateTimeWindowAsync(DaysOfWeekFlags days, TimeOnly start, TimeOnly end, CancellationToken cancellationToken)
    {
        db.FavoriteTimeWindows.Add(new FavoriteTimeWindow
        {
            DaysOfWeek = days,
            StartTime = start,
            EndTime = end,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateTimeWindowAsync(int id, DaysOfWeekFlags days, TimeOnly start, TimeOnly end, CancellationToken cancellationToken)
    {
        var window = await db.FavoriteTimeWindows.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (window is null)
        {
            return;
        }

        window.DaysOfWeek = days;
        window.StartTime = start;
        window.EndTime = end;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteTimeWindowAsync(int id, CancellationToken cancellationToken)
    {
        var window = await db.FavoriteTimeWindows.SingleOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (window is null)
        {
            return;
        }

        db.FavoriteTimeWindows.Remove(window);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CreateSeatMatrixAsync(
        int roomId,
        int? filmId,
        string name,
        string rowStart,
        string rowEnd,
        int seatNumberStart,
        int seatNumberEnd,
        int partySize,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        db.FavoriteSeatMatrices.Add(new FavoriteSeatMatrix
        {
            RoomId = roomId,
            FilmId = filmId,
            Name = name,
            RowStart = rowStart,
            RowEnd = rowEnd,
            SeatNumberStart = seatNumberStart,
            SeatNumberEnd = seatNumberEnd,
            PartySize = partySize,
            IsEnabled = isEnabled,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateSeatMatrixAsync(
        int id,
        int roomId,
        int? filmId,
        string name,
        string rowStart,
        string rowEnd,
        int seatNumberStart,
        int seatNumberEnd,
        int partySize,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var matrix = await db.FavoriteSeatMatrices.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (matrix is null)
        {
            return;
        }

        matrix.RoomId = roomId;
        matrix.FilmId = filmId;
        matrix.Name = name;
        matrix.RowStart = rowStart;
        matrix.RowEnd = rowEnd;
        matrix.SeatNumberStart = seatNumberStart;
        matrix.SeatNumberEnd = seatNumberEnd;
        matrix.PartySize = partySize;
        matrix.IsEnabled = isEnabled;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteSeatMatrixAsync(int id, CancellationToken cancellationToken)
    {
        var matrix = await db.FavoriteSeatMatrices.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (matrix is null)
        {
            return;
        }

        db.FavoriteSeatMatrices.Remove(matrix);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetSeatMatrixEnabledAsync(int id, bool enabled, CancellationToken cancellationToken)
    {
        var matrix = await db.FavoriteSeatMatrices.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (matrix is null)
        {
            return;
        }

        matrix.IsEnabled = enabled;
        await db.SaveChangesAsync(cancellationToken);
    }
}
