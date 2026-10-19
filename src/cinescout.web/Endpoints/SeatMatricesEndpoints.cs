using cinescout.contracts;
using cinescout.core.Preferences;
using cinescout.persistence;
using cinescout.web.Mapping;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Endpoints;

/// <summary>
/// The Seat Matrices screen's API surface (Technical Design Spec.md §5.2, #94): the page read
/// (cinemas/rooms/films/matrices) queries CineScoutDbContext directly and maps to DTOs — this
/// endpoint's own read model, independent of CinemasEndpoints, per #83's "each Endpoints/ file is
/// self-contained" rule. Writes go through PreferenceService, matching every other screen's
/// read/write split.
/// </summary>
public static class SeatMatricesEndpointsExtensions
{
    public static RouteGroupBuilder MapSeatMatricesEndpoints(this RouteGroupBuilder apiGroup)
    {
        var group = apiGroup.MapGroup("/seat-matrices");

        group.MapGet("/", GetPageAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
        group.MapPost("/{id:int}/toggle", ToggleAsync);

        return apiGroup;
    }

    private static async Task<Ok<SeatMatricesPageDto>> GetPageAsync(CineScoutDbContext db, CancellationToken cancellationToken)
    {
        var cinemas = await db.Cinemas
            .OrderBy(c => c.Name)
            .Select(c => new CinemaOptionDto { Id = c.Id, Name = c.Name })
            .ToListAsync(cancellationToken);

        var rooms = await db.Rooms
            .OrderBy(r => r.Name)
            .Select(r => new RoomOptionDto { Id = r.Id, CinemaId = r.CinemaId, Name = r.Name })
            .ToListAsync(cancellationToken);

        var films = await db.Films
            .OrderBy(f => f.Title)
            .Select(f => new FilmOptionDto { Id = f.Id, Title = f.Title })
            .ToListAsync(cancellationToken);

        var matrices = await db.FavoriteSeatMatrices
            .OrderBy(m => m.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new SeatMatricesPageDto
        {
            Cinemas = cinemas,
            Rooms = rooms,
            Films = films,
            Matrices = matrices.Select(SeatMatrixMapper.ToDto).ToList(),
        });
    }

    private static async Task<Results<Created<SeatMatrixDto>, ProblemHttpResult>> CreateAsync(
        SeatMatrixWriteRequest request, PreferenceService preferenceService, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        if (SeatMatrixValidation.Validate(request) is { } error)
        {
            return TypedResults.Problem(title: "Invalid seat matrix.", detail: error, statusCode: StatusCodes.Status400BadRequest);
        }

        var room = await db.Rooms.SingleOrDefaultAsync(r => r.Id == request.RoomId, cancellationToken);
        if (room is null)
        {
            return RoomNotFoundProblem();
        }

        if (request.FilmId is not null && !await db.Films.AnyAsync(f => f.Id == request.FilmId, cancellationToken))
        {
            return FilmNotFoundProblem();
        }

        var (outcome, id) = await preferenceService.CreateSeatMatrixAsync(ToInput(request, isEnabled: true), cancellationToken);
        if (outcome == SeatMatrixSaveOutcome.DuplicateGeneralMatrixForRoom)
        {
            return DuplicateGeneralMatrixProblem(room.Name);
        }

        var matrix = await db.FavoriteSeatMatrices.SingleOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Seat matrix {id} was just created but could not be re-read.");
        return TypedResults.Created($"/api/seat-matrices/{id}", SeatMatrixMapper.ToDto(matrix));
    }

    private static async Task<Results<Ok<SeatMatrixDto>, NotFound, ProblemHttpResult>> UpdateAsync(
        int id, SeatMatrixWriteRequest request, PreferenceService preferenceService, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        if (SeatMatrixValidation.Validate(request) is { } error)
        {
            return TypedResults.Problem(title: "Invalid seat matrix.", detail: error, statusCode: StatusCodes.Status400BadRequest);
        }

        var existing = await db.FavoriteSeatMatrices.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (existing is null)
        {
            return TypedResults.NotFound();
        }

        var room = await db.Rooms.SingleOrDefaultAsync(r => r.Id == request.RoomId, cancellationToken);
        if (room is null)
        {
            return RoomNotFoundProblem();
        }

        if (request.FilmId is not null && !await db.Films.AnyAsync(f => f.Id == request.FilmId, cancellationToken))
        {
            return FilmNotFoundProblem();
        }

        // IsEnabled has no editor control (§5.2) — preserve the existing value rather than the
        // create-only "always true" default; it's toggled only via ToggleAsync below.
        var outcome = await preferenceService.UpdateSeatMatrixAsync(id, ToInput(request, existing.IsEnabled), cancellationToken);
        if (outcome == SeatMatrixSaveOutcome.DuplicateGeneralMatrixForRoom)
        {
            return DuplicateGeneralMatrixProblem(room.Name);
        }

        var matrix = await db.FavoriteSeatMatrices.SingleOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Seat matrix {id} existed a moment ago but could not be re-read after update.");
        return TypedResults.Ok(SeatMatrixMapper.ToDto(matrix));
    }

    private static async Task<NoContent> DeleteAsync(int id, PreferenceService preferenceService, CancellationToken cancellationToken)
    {
        await preferenceService.DeleteSeatMatrixAsync(id, cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<SeatMatrixDto>, NotFound>> ToggleAsync(
        int id, PreferenceService preferenceService, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        var matrix = await db.FavoriteSeatMatrices.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (matrix is null)
        {
            return TypedResults.NotFound();
        }

        await preferenceService.SetSeatMatrixEnabledAsync(id, !matrix.IsEnabled, cancellationToken);

        var updated = await db.FavoriteSeatMatrices.SingleOrDefaultAsync(m => m.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Seat matrix {id} existed a moment ago but could not be re-read after toggling.");
        return TypedResults.Ok(SeatMatrixMapper.ToDto(updated));
    }

    private static SeatMatrixInput ToInput(SeatMatrixWriteRequest request, bool isEnabled) => new(
        request.RoomId, request.FilmId, request.Name, request.RowStart, request.RowEnd,
        request.SeatNumberStart, request.SeatNumberEnd, request.PartySize, isEnabled);

    private static ProblemHttpResult RoomNotFoundProblem() =>
        TypedResults.Problem(title: "Room not found.", statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult FilmNotFoundProblem() =>
        TypedResults.Problem(title: "Film not found.", statusCode: StatusCodes.Status400BadRequest);

    private static ProblemHttpResult DuplicateGeneralMatrixProblem(string roomName) => TypedResults.Problem(
        title: "Room already has a general preference.",
        detail: $"{roomName} already has a general preference — edit that one instead.",
        statusCode: StatusCodes.Status409Conflict);
}
