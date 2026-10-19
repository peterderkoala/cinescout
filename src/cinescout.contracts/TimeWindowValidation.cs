namespace cinescout.contracts;

/// <summary>
/// Ports the two rules the legacy static-SSR <c>TimePreferences.razor</c> page's <c>@code</c> block
/// used to own ("select at least one day," "end time after start time" — issue #78's resolution) into
/// one place both projects can call, per <see cref="CinemaValidation"/>'s precedent: a shared leaf-project
/// validator avoids the server endpoint and the WASM editor silently drifting apart.
/// </summary>
public static class TimeWindowValidation
{
    public static string? Validate(TimeWindowWriteRequest request)
    {
        if (request.DayCodes.Length == 0)
        {
            return "Select at least one day.";
        }

        if (request.EndTime <= request.StartTime)
        {
            return "End time must be later than start time.";
        }

        return null;
    }
}
