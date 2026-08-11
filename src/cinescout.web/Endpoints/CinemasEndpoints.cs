using cinescout.contracts;
using cinescout.core.Cinemas;
using cinescout.core.Kinoheld;
using cinescout.persistence;
using cinescout.web.Mapping;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Endpoints;

/// <summary>
/// The Cinemas screen's API surface (Technical Design Spec.md §5.1, ADR 0005): reads query
/// CineScoutDbContext directly and map to DTOs, writes go through CinemaService /
/// KinoheldRoomSeedingService — matching every other screen's read/write split (#83).
/// </summary>
public static class CinemasEndpointsExtensions
{
    public static RouteGroupBuilder MapCinemasEndpoints(this RouteGroupBuilder apiGroup)
    {
        var group = apiGroup.MapGroup("/cinemas");

        group.MapGet("/", GetListAsync);
        group.MapGet("/{id:int}", GetDetailAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);
        group.MapPost("/{id:int}/reseed-rooms", ReseedRoomsAsync);
        group.MapPut("/{cinemaId:int}/rooms/{roomId:int}", RenameRoomAsync);

        return apiGroup;
    }

    private static async Task<Ok<IReadOnlyList<CinemaListItemDto>>> GetListAsync(CineScoutDbContext db, CancellationToken cancellationToken)
    {
        var cinemas = await db.Cinemas
            .OrderBy(c => c.Name)
            .Select(c => new CinemaListItemDto
            {
                Id = c.Id,
                Name = c.Name,
                ExternalCinemaId = c.ExternalCinemaId,
                IsActive = c.IsActive,
                RoomCount = db.Rooms.Count(r => r.CinemaId == c.Id),
            })
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<CinemaListItemDto>>(cinemas);
    }

    private static async Task<Results<Ok<CinemaDetailDto>, NotFound>> GetDetailAsync(int id, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        var dto = await BuildDetailDtoAsync(db, id, cancellationToken);

        return dto is null ? TypedResults.NotFound() : TypedResults.Ok(dto);
    }

    private static async Task<Results<Created<CinemaDetailDto>, ProblemHttpResult>> CreateAsync(
        CinemaWriteRequest request, CinemaService cinemaService, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        if (CinemaValidation.Validate(request) is { } error)
        {
            return TypedResults.Problem(title: "Invalid cinema.", detail: error, statusCode: StatusCodes.Status400BadRequest);
        }

        var (outcome, id) = await cinemaService.CreateAsync(ToInput(request), cancellationToken);
        if (outcome == CinemaSaveOutcome.DuplicateExternalCinemaId)
        {
            return DuplicateExternalCinemaIdProblem();
        }

        var dto = await BuildDetailDtoAsync(db, id, cancellationToken)
            ?? throw new InvalidOperationException($"Cinema {id} was just created but could not be re-read.");

        return TypedResults.Created($"/api/cinemas/{id}", dto);
    }

    private static async Task<Results<Ok<CinemaDetailDto>, NotFound, ProblemHttpResult>> UpdateAsync(
        int id, CinemaWriteRequest request, CinemaService cinemaService, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        if (CinemaValidation.Validate(request) is { } error)
        {
            return TypedResults.Problem(title: "Invalid cinema.", detail: error, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await db.Cinemas.AnyAsync(c => c.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var outcome = await cinemaService.UpdateAsync(id, ToInput(request), cancellationToken);
        if (outcome == CinemaSaveOutcome.DuplicateExternalCinemaId)
        {
            return DuplicateExternalCinemaIdProblem();
        }

        var dto = await BuildDetailDtoAsync(db, id, cancellationToken)
            ?? throw new InvalidOperationException($"Cinema {id} existed a moment ago but could not be re-read after update.");

        return TypedResults.Ok(dto);
    }

    private static ProblemHttpResult DuplicateExternalCinemaIdProblem() => TypedResults.Problem(
        title: "External cinema id already in use.",
        detail: "Another cinema already has this external cinema id. Each cinema's id must be unique.",
        statusCode: StatusCodes.Status409Conflict);

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(int id, CinemaService cinemaService, CancellationToken cancellationToken)
    {
        var outcome = await cinemaService.DeleteAsync(id, cancellationToken);

        return outcome switch
        {
            CinemaDeleteOutcome.Deleted => TypedResults.NoContent(),
            CinemaDeleteOutcome.BlockedHasRooms => TypedResults.Problem(
                title: "Cinema has rooms.",
                detail: "This cinema has at least one seeded Room and can't be deleted — deleting would also erase its crawl/match/notification history. Use the Crawling switch to pause it instead.",
                statusCode: StatusCodes.Status409Conflict),
            _ => throw new InvalidOperationException($"Unhandled {nameof(CinemaDeleteOutcome)} value {outcome}."),
        };
    }

    private static async Task<Results<Ok<CinemaDetailDto>, ProblemHttpResult>> ReseedRoomsAsync(
        int id, KinoheldRoomSeedingService roomSeedingService, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        var cinema = await db.Cinemas.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cinema is null)
        {
            return TypedResults.Problem(title: "Cinema not found.", statusCode: StatusCodes.Status404NotFound);
        }

        var outcome = await roomSeedingService.SeedRoomsForCinemaAsync(cinema, cancellationToken);

        switch (outcome)
        {
            case RoomSeedOutcome.Seeded:
                var dto = await BuildDetailDtoAsync(db, id, cancellationToken)
                    ?? throw new InvalidOperationException($"Cinema {id} was just re-seeded but could not be re-read.");
                return TypedResults.Ok(dto);

            case RoomSeedOutcome.CircuitOpen:
                return ProblemWithKinoheldStatus(
                    "breaker-open",
                    "Kinoheld connection unavailable.",
                    "The Kinoheld connection's circuit breaker is open — all Kinoheld polling is stopped until the app restarts.",
                    StatusCodes.Status503ServiceUnavailable);

            case RoomSeedOutcome.CooldownActive:
                return ProblemWithKinoheldStatus(
                    "cooldown",
                    "Re-seeded too recently.",
                    "Re-seed rooms is available again shortly — it was already run for this cinema within the last few minutes.",
                    StatusCodes.Status429TooManyRequests);

            case RoomSeedOutcome.Unavailable:
                return TypedResults.Problem(
                    title: "Nothing crawled yet.",
                    detail: "This cinema has no crawled performances yet, so there's no widget URL to derive rooms from. Rooms appear once a Hall-of-Fame crawl has run.",
                    statusCode: StatusCodes.Status409Conflict);

            default:
                throw new InvalidOperationException($"Unhandled {nameof(RoomSeedOutcome)} value {outcome}.");
        }
    }

    private static async Task<Results<Ok<RoomDto>, NotFound>> RenameRoomAsync(
        int cinemaId, int roomId, RoomRenameRequest request, CinemaService cinemaService, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Rooms.AnyAsync(r => r.Id == roomId && r.CinemaId == cinemaId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        await cinemaService.RenameRoomAsync(roomId, request.Name, cancellationToken);

        var room = await db.Rooms.SingleAsync(r => r.Id == roomId, cancellationToken);
        return TypedResults.Ok(RoomMapper.ToDto(room));
    }

    private static async Task<CinemaDetailDto?> BuildDetailDtoAsync(CineScoutDbContext db, int id, CancellationToken cancellationToken)
    {
        var cinema = await db.Cinemas.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (cinema is null)
        {
            return null;
        }

        var rooms = await db.Rooms
            .Where(r => r.CinemaId == id)
            .OrderBy(r => r.Name)
            .Select(r => new RoomDto { Id = r.Id, CinemaId = r.CinemaId, ExternalAuditoriumId = r.ExternalAuditoriumId, Name = r.Name })
            .ToListAsync(cancellationToken);

        return new CinemaDetailDto
        {
            Id = cinema.Id,
            Name = cinema.Name,
            ExternalCinemaId = cinema.ExternalCinemaId,
            CrawlBaseUrl = cinema.CrawlBaseUrl,
            KinoheldCinemaId = cinema.KinoheldCinemaId,
            IsActive = cinema.IsActive,
            LastCrawlAt = cinema.LastCrawlAt,
            Rooms = rooms,
        };
    }

    private static CinemaInput ToInput(CinemaWriteRequest request) =>
        new(request.Name, request.ExternalCinemaId, request.CrawlBaseUrl, request.IsActive);

    private static ProblemHttpResult ProblemWithKinoheldStatus(string kinoheldStatus, string title, string detail, int statusCode) =>
        TypedResults.Problem(
            title: title,
            detail: detail,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["kinoheldStatus"] = kinoheldStatus });
}
