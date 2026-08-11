using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.Preferences;

/// <summary>
/// The full editable field set of a <see cref="FavoriteSeatMatrix"/>, bundled so create/update
/// call cinemas can't silently misorder nine positional arguments.
/// </summary>
public sealed record SeatMatrixInput(
    int RoomId,
    int? FilmId,
    string Name,
    string RowStart,
    string RowEnd,
    int SeatNumberStart,
    int SeatNumberEnd,
    int PartySize,
    bool IsEnabled);

/// <summary>Outcome of a seat-matrix create/update attempt (#94's deepening, per #78's resolution).</summary>
public enum SeatMatrixSaveOutcome
{
    Saved,

    /// <summary>
    /// Rejected: the room already has a different FilmId-null (general) matrix. The General tab
    /// shows at most one such card per room, so a second one would be unreachable in the UI —
    /// not editable, toggleable, or deletable.
    /// </summary>
    DuplicateGeneralMatrixForRoom,
}

/// <summary>
/// Thin persistence layer for the user's preference entities (FavoriteTimeWindow,
/// FavoriteSeatMatrix). Validation is the UI's job; like TrackedMovieService, operations
/// on missing ids are defensive no-ops rather than errors.
/// </summary>
public sealed class PreferenceService(CineScoutDbContext db)
{
    public async Task<int> CreateTimeWindowAsync(DaysOfWeekFlags days, TimeOnly start, TimeOnly end, CancellationToken cancellationToken)
    {
        var window = new FavoriteTimeWindow
        {
            DaysOfWeek = days,
            StartTime = start,
            EndTime = end,
        };
        db.FavoriteTimeWindows.Add(window);
        await db.SaveChangesAsync(cancellationToken);

        return window.Id;
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

    public async Task<(SeatMatrixSaveOutcome Outcome, int Id)> CreateSeatMatrixAsync(SeatMatrixInput input, CancellationToken cancellationToken)
    {
        if (input.FilmId is null && await HasOtherGeneralMatrixAsync(input.RoomId, excludeId: null, cancellationToken))
        {
            return (SeatMatrixSaveOutcome.DuplicateGeneralMatrixForRoom, 0);
        }

        var matrix = new FavoriteSeatMatrix
        {
            RoomId = input.RoomId,
            FilmId = input.FilmId,
            Name = input.Name,
            RowStart = input.RowStart,
            RowEnd = input.RowEnd,
            SeatNumberStart = input.SeatNumberStart,
            SeatNumberEnd = input.SeatNumberEnd,
            PartySize = input.PartySize,
            IsEnabled = input.IsEnabled,
        };
        db.FavoriteSeatMatrices.Add(matrix);
        await db.SaveChangesAsync(cancellationToken);

        return (SeatMatrixSaveOutcome.Saved, matrix.Id);
    }

    public async Task<SeatMatrixSaveOutcome> UpdateSeatMatrixAsync(int id, SeatMatrixInput input, CancellationToken cancellationToken)
    {
        var matrix = await db.FavoriteSeatMatrices.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (matrix is null)
        {
            return SeatMatrixSaveOutcome.Saved; // defensive no-op, same posture as every other operation here
        }

        if (input.FilmId is null && await HasOtherGeneralMatrixAsync(input.RoomId, excludeId: id, cancellationToken))
        {
            return SeatMatrixSaveOutcome.DuplicateGeneralMatrixForRoom;
        }

        matrix.RoomId = input.RoomId;
        matrix.FilmId = input.FilmId;
        matrix.Name = input.Name;
        matrix.RowStart = input.RowStart;
        matrix.RowEnd = input.RowEnd;
        matrix.SeatNumberStart = input.SeatNumberStart;
        matrix.SeatNumberEnd = input.SeatNumberEnd;
        matrix.PartySize = input.PartySize;
        matrix.IsEnabled = input.IsEnabled;
        await db.SaveChangesAsync(cancellationToken);

        return SeatMatrixSaveOutcome.Saved;
    }

    private Task<bool> HasOtherGeneralMatrixAsync(int roomId, int? excludeId, CancellationToken cancellationToken) =>
        db.FavoriteSeatMatrices.AnyAsync(m => m.RoomId == roomId && m.FilmId == null && m.Id != excludeId, cancellationToken);

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
