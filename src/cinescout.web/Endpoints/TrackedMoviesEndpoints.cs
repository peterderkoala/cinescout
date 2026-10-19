using cinescout.contracts;
using cinescout.core.Matching;
using cinescout.core.TrackedMovies;
using cinescout.model;
using cinescout.persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Endpoints;

/// <summary>
/// The Tracked Movies screen's API surface (Technical Design Spec.md §5.5, #95): own read model,
/// independent of HomeEndpoints, per #83's "each Endpoints/ file is self-contained" rule — status
/// badge is recomputed here the same way, via <see cref="SeatAvailabilityQuery"/>'s batch overload
/// (the same one Home's own Tracked Movies panel already uses). Track/Untrack call through
/// unchanged to <see cref="TrackedMovieService"/> (defensive no-op semantics preserved) and return
/// the full page again, since a single action moves a film between both lists.
/// </summary>
public static class TrackedMoviesEndpointsExtensions
{
    public static RouteGroupBuilder MapTrackedMoviesEndpoints(this RouteGroupBuilder apiGroup)
    {
        var group = apiGroup.MapGroup("/tracked-movies");

        group.MapGet("/", GetPageAsync);
        group.MapPost("/{filmId:int}/track", TrackAsync);
        group.MapPost("/{filmId:int}/untrack", UntrackAsync);

        return apiGroup;
    }

    private static async Task<Ok<TrackedMoviesPageDto>> GetPageAsync(
        CineScoutDbContext db, SeatAvailabilityQuery seatAvailability, CancellationToken cancellationToken) =>
        TypedResults.Ok(await BuildPageAsync(db, seatAvailability, cancellationToken));

    private static async Task<Results<Ok<TrackedMoviesPageDto>, ProblemHttpResult>> TrackAsync(
        int filmId, TrackedMovieService trackedMovieService, CineScoutDbContext db, SeatAvailabilityQuery seatAvailability, CancellationToken cancellationToken)
    {
        if (!await db.Films.AnyAsync(f => f.Id == filmId, cancellationToken))
        {
            return FilmNotFoundProblem();
        }

        await trackedMovieService.TrackAsync(filmId, cancellationToken);

        return TypedResults.Ok(await BuildPageAsync(db, seatAvailability, cancellationToken));
    }

    private static async Task<Results<Ok<TrackedMoviesPageDto>, ProblemHttpResult>> UntrackAsync(
        int filmId, TrackedMovieService trackedMovieService, CineScoutDbContext db, SeatAvailabilityQuery seatAvailability, CancellationToken cancellationToken)
    {
        if (!await db.Films.AnyAsync(f => f.Id == filmId, cancellationToken))
        {
            return FilmNotFoundProblem();
        }

        await trackedMovieService.UntrackAsync(filmId, cancellationToken);

        return TypedResults.Ok(await BuildPageAsync(db, seatAvailability, cancellationToken));
    }

    private static ProblemHttpResult FilmNotFoundProblem() =>
        TypedResults.Problem(title: "Film not found.", statusCode: StatusCodes.Status404NotFound);

    private static async Task<TrackedMoviesPageDto> BuildPageAsync(CineScoutDbContext db, SeatAvailabilityQuery seatAvailability, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        return new TrackedMoviesPageDto
        {
            TrackedMovies = await BuildTrackedMoviesAsync(db, seatAvailability, now, cancellationToken),
            AllFilms = await BuildAllFilmsAsync(db, now, cancellationToken),
        };
    }

    private static async Task<IReadOnlyList<TrackedMovieRowDto>> BuildTrackedMoviesAsync(
        CineScoutDbContext db, SeatAvailabilityQuery seatAvailability, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tracked = await db.TrackedMovies
            .Join(db.Films, tm => tm.FilmId, f => f.Id, (tm, f) => new { f.Id, f.Title })
            .OrderBy(x => x.Title)
            .ToListAsync(cancellationToken);

        if (tracked.Count == 0)
        {
            return [];
        }

        var filmIds = tracked.Select(t => t.Id).ToList();
        var performances = await db.Performances
            .Where(p => filmIds.Contains(p.FilmId) && p.StartsAt >= now && p.Status != PerformanceStatus.Cancelled)
            .OrderBy(p => p.StartsAt)
            .ToListAsync(cancellationToken);

        var hasOpenSeats = await seatAvailability.HasOpenFavoriteMatrixSeatsAsync(performances, cancellationToken);

        var roomIds = performances.Where(p => p.RoomId is not null).Select(p => p.RoomId!.Value).Distinct().ToList();
        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);

        var performancesByFilm = performances.ToLookup(p => p.FilmId);

        var rows = new List<TrackedMovieRowDto>();
        foreach (var film in tracked)
        {
            var cards = performancesByFilm[film.Id]
                .Select(p => new FilmPerformanceCardModel
                {
                    PerformanceId = p.Id,
                    Title = film.Title,
                    Cinema = null,
                    Room = p.RoomId is int roomId && rooms.TryGetValue(roomId, out var room) ? room.Name : "",
                    DateTime = PerformanceDateTimeFormatting.FormatListDateTime(CinemaTimeZone.ToLocal(p.StartsAt).DateTime),
                    Price = null,
                    Status = DeriveStatus(p, hasOpenSeats.GetValueOrDefault(p.Id)),
                    Tracking = true,
                    IsMatch = false,
                    MatchReasons = [],
                    PosterUrl = null,
                    BookingLink = p.BookingLink,
                })
                .ToList();

            rows.Add(new TrackedMovieRowDto { FilmId = film.Id, Title = film.Title, Performances = cards });
        }

        return rows;
    }

    private static async Task<IReadOnlyList<UntrackedFilmDto>> BuildAllFilmsAsync(CineScoutDbContext db, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var trackedFilmIds = db.TrackedMovies.Select(t => t.FilmId);
        var filmIdsWithUpcomingPerformance = db.Performances
            .Where(p => p.StartsAt >= now && p.Status != PerformanceStatus.Cancelled)
            .Select(p => p.FilmId)
            .Distinct();

        return await db.Films
            .Where(f => filmIdsWithUpcomingPerformance.Contains(f.Id) && !trackedFilmIds.Contains(f.Id))
            .OrderBy(f => f.Title)
            .Select(f => new UntrackedFilmDto { FilmId = f.Id, Title = f.Title })
            .ToListAsync(cancellationToken);
    }

    /// <summary>§6.1's precedence: Cancelled beats sold-out beats seats-available beats no badge.</summary>
    private static PerformanceCardStatus DeriveStatus(Performance performance, bool hasSufficientSeats) => performance switch
    {
        { Status: PerformanceStatus.Cancelled } => PerformanceCardStatus.Cancelled,
        { IsSoldOut: true } => PerformanceCardStatus.SoldOut,
        _ when hasSufficientSeats => PerformanceCardStatus.Available,
        _ => PerformanceCardStatus.None,
    };
}
