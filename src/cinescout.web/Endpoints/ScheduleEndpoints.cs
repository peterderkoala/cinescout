using cinescout.contracts;
using cinescout.core.Matching;
using cinescout.model;
using cinescout.persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Endpoints;

/// <summary>
/// The Schedule screen's API surface (Technical Design Spec.md §5.4, #96): a 14-day, day-grouped
/// window of every <c>Performance</c> (not filtered to tracked films — this is the full listing,
/// unlike Tracked Movies/Home), with Previous/Next-week paging. Per #78's resolution, this ticket's
/// windowing/grouping is design intent to build, not code ported from the old flat, ungrouped
/// <c>Schedule.razor</c> stub.
///
/// The window is computed in Berlin-local calendar days (<see cref="CinemaTimeZone"/>), not UTC —
/// day boundaries matter here (which group a performance falls into) in a way the simple "&gt;= now"
/// future-filter other screens use doesn't need to worry about.
/// </summary>
public static class ScheduleEndpointsExtensions
{
    public static RouteGroupBuilder MapScheduleEndpoints(this RouteGroupBuilder apiGroup)
    {
        apiGroup.MapGet("/schedule", GetAsync);

        return apiGroup;
    }

    private static async Task<Ok<ScheduleDto>> GetAsync(
        int? weekOffset, CineScoutDbContext db, SeatAvailabilityQuery seatAvailability, CancellationToken cancellationToken)
    {
        // Defensive clamp, same posture as TrackedMovieService's track/untrack no-ops: a negative
        // offset (before "today") isn't a state the UI ever navigates to, so fold it back to 0
        // rather than erroring.
        var offset = Math.Max(weekOffset ?? 0, 0);

        var todayLocal = DateOnly.FromDateTime(CinemaTimeZone.ToLocal(DateTimeOffset.UtcNow).DateTime);
        var windowStart = todayLocal.AddDays(offset * 7);
        var windowEndInclusive = windowStart.AddDays(13);
        var windowEndExclusive = windowStart.AddDays(14);

        var windowStartInstant = ToInstant(windowStart);
        var windowEndExclusiveInstant = ToInstant(windowEndExclusive);

        var performances = await db.Performances
            .Where(p => p.StartsAt >= windowStartInstant && p.StartsAt < windowEndExclusiveInstant)
            .OrderBy(p => p.StartsAt)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new ScheduleDto
        {
            WeekLabel = PerformanceDateTimeFormatting.FormatWeekCaption(windowStart, windowEndInclusive),
            WeekOffset = offset,
            Days = await BuildDaysAsync(db, seatAvailability, performances, todayLocal, cancellationToken),
        });
    }

    /// <summary>
    /// Npgsql's <c>timestamp with time zone</c> mapping only accepts offset-zero <see cref="DateTimeOffset"/>
    /// values, so the Berlin-offset instant built from the local calendar date must be normalized via
    /// <see cref="DateTimeOffset.ToUniversalTime"/> before it can be used in a query against <c>StartsAt</c>.
    /// </summary>
    private static DateTimeOffset ToInstant(DateOnly localDate)
    {
        var midnight = localDate.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(midnight, CinemaTimeZone.Berlin.GetUtcOffset(midnight)).ToUniversalTime();
    }

    private static async Task<IReadOnlyList<ScheduleDayDto>> BuildDaysAsync(
        CineScoutDbContext db, SeatAvailabilityQuery seatAvailability, IReadOnlyList<Performance> performances, DateOnly todayLocal, CancellationToken cancellationToken)
    {
        if (performances.Count == 0)
        {
            return [];
        }

        var filmIds = performances.Select(p => p.FilmId).Distinct().ToList();
        var films = await db.Films.Where(f => filmIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, cancellationToken);

        var roomIds = performances.Where(p => p.RoomId is not null).Select(p => p.RoomId!.Value).Distinct().ToList();
        var rooms = await db.Rooms.Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);

        var trackedFilmIds = await db.TrackedMovies.Where(t => filmIds.Contains(t.FilmId)).Select(t => t.FilmId).ToListAsync(cancellationToken);
        var trackedFilmIdSet = trackedFilmIds.ToHashSet();

        var hasOpenSeats = await seatAvailability.HasOpenFavoriteMatrixSeatsAsync(performances, cancellationToken);

        return performances
            .Select(p => (LocalStart: CinemaTimeZone.ToLocal(p.StartsAt), Performance: p))
            .GroupBy(x => DateOnly.FromDateTime(x.LocalStart.DateTime))
            .OrderBy(g => g.Key)
            .Select(g => new ScheduleDayDto
            {
                Label = PerformanceDateTimeFormatting.FormatDayHeading(g.Key, todayLocal),
                Performances = g
                    .Select(x => new FilmPerformanceCardModel
                    {
                        PerformanceId = x.Performance.Id,
                        Title = films[x.Performance.FilmId].Title,
                        Cinema = null,
                        Room = x.Performance.RoomId is int roomId && rooms.TryGetValue(roomId, out var room) ? room.Name : "",
                        DateTime = PerformanceDateTimeFormatting.FormatTimeOnly(x.LocalStart.DateTime),
                        Price = null,
                        Status = DeriveStatus(x.Performance, hasOpenSeats.GetValueOrDefault(x.Performance.Id)),
                        Tracking = trackedFilmIdSet.Contains(x.Performance.FilmId),
                        IsMatch = false,
                        MatchReasons = [],
                        PosterUrl = null,
                        BookingLink = x.Performance.BookingLink,
                    })
                    .ToList(),
            })
            .ToList();
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
