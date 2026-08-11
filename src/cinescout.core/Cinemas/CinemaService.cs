using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.Cinemas;

/// <summary>The full editable field set of a <see cref="Cinema"/>, bundled per <see cref="Preferences.SeatMatrixInput"/>'s precedent.</summary>
public sealed record CinemaInput(string Name, string ExternalCinemaId, string CrawlBaseUrl, bool IsActive);

/// <summary>Outcome of a delete attempt — the one CinemaService operation that isn't a bare no-op/success (ADR 0005).</summary>
public enum CinemaDeleteOutcome
{
    /// <summary>The cinema was deleted, or didn't exist to begin with (defensive no-op, same posture as every other operation here).</summary>
    Deleted,

    /// <summary>Rejected: the cinema has at least one Room row, meaning a successful seed happened and the cascade would erase real history (ADR 0005). Use Cinema.IsActive to pause crawling instead.</summary>
    BlockedHasRooms,
}

/// <summary>Outcome of a create/update attempt — Cinema.ExternalCinemaId has a unique index, so a save can be rejected without ever reaching the database.</summary>
public enum CinemaSaveOutcome
{
    Saved,

    /// <summary>Rejected: another cinema already has this ExternalCinemaId. A pre-check, not a caught DbUpdateException — this is a single-operator app with no realistic concurrent-create race to worry about.</summary>
    DuplicateExternalCinemaId,
}

/// <summary>
/// Thin persistence layer for Cinema (and its Rooms' one user-editable field, Name) — mirrors
/// TrackedMovieService/PreferenceService's shape: constructor-scoped over CineScoutDbContext,
/// defensive no-ops on missing ids. Reads stay direct CineScoutDbContext queries in
/// CinemasEndpoints; only writes go through this service (ADR 0005).
/// </summary>
public sealed class CinemaService(CineScoutDbContext db)
{
    public async Task<(CinemaSaveOutcome Outcome, int Id)> CreateAsync(CinemaInput input, CancellationToken cancellationToken)
    {
        if (await db.Cinemas.AnyAsync(c => c.ExternalCinemaId == input.ExternalCinemaId, cancellationToken))
        {
            return (CinemaSaveOutcome.DuplicateExternalCinemaId, 0);
        }

        var cinema = new Cinema
        {
            Name = input.Name,
            ExternalCinemaId = input.ExternalCinemaId,
            CrawlBaseUrl = input.CrawlBaseUrl,
            IsActive = input.IsActive,
        };
        db.Cinemas.Add(cinema);
        await db.SaveChangesAsync(cancellationToken);

        return (CinemaSaveOutcome.Saved, cinema.Id);
    }

    public async Task<CinemaSaveOutcome> UpdateAsync(int id, CinemaInput input, CancellationToken cancellationToken)
    {
        var cinema = await db.Cinemas.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cinema is null)
        {
            return CinemaSaveOutcome.Saved; // defensive no-op, same posture as every other operation here
        }

        if (await db.Cinemas.AnyAsync(c => c.ExternalCinemaId == input.ExternalCinemaId && c.Id != id, cancellationToken))
        {
            return CinemaSaveOutcome.DuplicateExternalCinemaId;
        }

        cinema.Name = input.Name;
        cinema.ExternalCinemaId = input.ExternalCinemaId;
        cinema.CrawlBaseUrl = input.CrawlBaseUrl;
        cinema.IsActive = input.IsActive;
        await db.SaveChangesAsync(cancellationToken);

        return CinemaSaveOutcome.Saved;
    }

    public async Task<CinemaDeleteOutcome> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var cinema = await db.Cinemas.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cinema is null)
        {
            return CinemaDeleteOutcome.Deleted;
        }

        var hasRooms = await db.Rooms.AnyAsync(r => r.CinemaId == id, cancellationToken);
        if (hasRooms)
        {
            return CinemaDeleteOutcome.BlockedHasRooms;
        }

        db.Cinemas.Remove(cinema);
        await db.SaveChangesAsync(cancellationToken);

        return CinemaDeleteOutcome.Deleted;
    }

    /// <summary>Room's one user-editable field (§5.1: ExternalAuditoriumId is provider-owned).</summary>
    public async Task RenameRoomAsync(int roomId, string name, CancellationToken cancellationToken)
    {
        var room = await db.Rooms.SingleOrDefaultAsync(r => r.Id == roomId, cancellationToken);
        if (room is null)
        {
            return;
        }

        room.Name = name;
        await db.SaveChangesAsync(cancellationToken);
    }
}
