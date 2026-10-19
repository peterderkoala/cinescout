namespace cinescout.contracts;

/// <summary>
/// Ports the pure (no-DB) rules the legacy static-SSR <c>SeatMatrices.razor</c> page's <c>@code</c>
/// block used to own (issue #78's resolution) into one place both projects can call, per
/// <see cref="CinemaValidation"/>/<see cref="TimeWindowValidation"/>'s precedent. Room/Film existence
/// and the one-general-matrix-per-room guard are DB-dependent and stay server-side
/// (<c>SeatMatricesEndpoints</c>/<c>PreferenceService</c>), not here.
/// </summary>
public static class SeatMatrixValidation
{
    public static string? Validate(SeatMatrixWriteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.RowStart) || string.IsNullOrWhiteSpace(request.RowEnd))
        {
            return "Both row bounds are required.";
        }

        if (string.CompareOrdinal(request.RowStart, request.RowEnd) > 0)
        {
            return "Row from must not come after row to.";
        }

        if (request.SeatNumberStart < 1 || request.SeatNumberEnd < request.SeatNumberStart)
        {
            return "Seat numbers must be at least 1, with seat from not after seat to.";
        }

        if (request.PartySize < 1)
        {
            return "Party size must be at least 1.";
        }

        return null;
    }
}
