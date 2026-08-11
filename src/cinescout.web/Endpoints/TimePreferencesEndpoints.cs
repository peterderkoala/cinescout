using cinescout.contracts;
using cinescout.core.Preferences;
using cinescout.model;
using cinescout.persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace cinescout.web.Endpoints;

/// <summary>
/// The Time Preferences screen's API surface (Technical Design Spec.md §5.3, #93): the list read
/// queries CineScoutDbContext directly and maps to DTOs, writes go through PreferenceService —
/// matching every other screen's read/write split (#83). First of the five existing screens
/// converted to WASM+API (#78's resolution) — the template the remaining ones (#94–#98) follow.
/// </summary>
public static class TimePreferencesEndpointsExtensions
{
    public static RouteGroupBuilder MapTimePreferencesEndpoints(this RouteGroupBuilder apiGroup)
    {
        var group = apiGroup.MapGroup("/time-preferences");

        group.MapGet("/", GetListAsync);
        group.MapPost("/", CreateAsync);
        group.MapPut("/{id:int}", UpdateAsync);
        group.MapDelete("/{id:int}", DeleteAsync);

        return apiGroup;
    }

    private static async Task<Ok<IReadOnlyList<TimeWindowDto>>> GetListAsync(CineScoutDbContext db, CancellationToken cancellationToken)
    {
        var windows = await db.FavoriteTimeWindows
            .OrderBy(w => w.StartTime)
            .ThenBy(w => w.Id)
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<TimeWindowDto>>(windows.Select(ToDto).ToList());
    }

    private static async Task<Results<Created<TimeWindowDto>, ProblemHttpResult>> CreateAsync(
        TimeWindowWriteRequest request, PreferenceService preferenceService, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        if (TimeWindowValidation.Validate(request) is { } error)
        {
            return TypedResults.Problem(title: "Invalid time window.", detail: error, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryConvertDayCodes(request.DayCodes, out var days, out var problem))
        {
            return problem;
        }

        var id = await preferenceService.CreateTimeWindowAsync(days, request.StartTime, request.EndTime, cancellationToken);

        var window = await db.FavoriteTimeWindows.SingleOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Time window {id} was just created but could not be re-read.");
        return TypedResults.Created($"/api/time-preferences/{id}", ToDto(window));
    }

    private static async Task<Results<Ok<TimeWindowDto>, NotFound, ProblemHttpResult>> UpdateAsync(
        int id, TimeWindowWriteRequest request, PreferenceService preferenceService, CineScoutDbContext db, CancellationToken cancellationToken)
    {
        if (TimeWindowValidation.Validate(request) is { } error)
        {
            return TypedResults.Problem(title: "Invalid time window.", detail: error, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!await db.FavoriteTimeWindows.AnyAsync(w => w.Id == id, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        if (!TryConvertDayCodes(request.DayCodes, out var days, out var problem))
        {
            return problem;
        }

        await preferenceService.UpdateTimeWindowAsync(id, days, request.StartTime, request.EndTime, cancellationToken);

        var window = await db.FavoriteTimeWindows.SingleOrDefaultAsync(w => w.Id == id, cancellationToken)
            ?? throw new InvalidOperationException($"Time window {id} existed a moment ago but could not be re-read after update.");
        return TypedResults.Ok(ToDto(window));
    }

    private static async Task<NoContent> DeleteAsync(int id, PreferenceService preferenceService, CancellationToken cancellationToken)
    {
        await preferenceService.DeleteTimeWindowAsync(id, cancellationToken);

        return TypedResults.NoContent();
    }

    /// <summary>
    /// DayCodeConverter.FromDayCodes throws ArgumentException on a code outside Mo/Tu/We/Th/Fr/Sa/Su
    /// — only reachable via a malformed request body, never the UI's own fixed set of day buttons, so
    /// it's translated into a 400 here rather than left to bubble up as an unhandled 500.
    /// </summary>
    private static bool TryConvertDayCodes(string[] dayCodes, out DaysOfWeekFlags days, out ProblemHttpResult problem)
    {
        try
        {
            days = (DaysOfWeekFlags)(int)DayCodeConverter.FromDayCodes(dayCodes);
            problem = null!;
            return true;
        }
        catch (ArgumentException ex)
        {
            days = default;
            problem = TypedResults.Problem(title: "Invalid time window.", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            return false;
        }
    }

    private static TimeWindowDto ToDto(FavoriteTimeWindow window) => new()
    {
        Id = window.Id,
        DayCodes = DayCodeConverter.ToDayCodes((Weekday)(int)window.DaysOfWeek),
        StartTime = window.StartTime,
        EndTime = window.EndTime,
    };
}
