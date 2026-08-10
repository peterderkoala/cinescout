using System.Globalization;

namespace cinescout.contracts;

/// <summary>
/// Timestamp convention for DTOs in this project: any timestamp crossing the wire is a
/// <see cref="DateTime"/> with <see cref="DateTimeKind.Unspecified"/> (Berlin-local, no UTC offset
/// — Blazor WASM's "local time zone" is the visitor's browser, not the server's), never a
/// <see cref="DateTimeOffset"/>.
///
/// The actual UTC→Berlin conversion is deliberately NOT done here: cinescout.core already owns that
/// logic (<c>cinescout.core.Matching.CinemaTimeZone</c>, which converts
/// <c>Performance.StartsAt</c>), and cinescout.contracts must never reference cinescout.core (true
/// leaf). Re-implementing a second, parallel Europe/Berlin conversion in this project would risk the
/// two drifting apart. Instead, this method takes the already-Berlin-local, Kind=Unspecified
/// <see cref="DateTime"/> as a plain input — the caller (e.g. cinescout.web, which can see both
/// cinescout.core and cinescout.contracts) is responsible for the conversion step, this method is
/// responsible only for the §7 display formatting.
/// </summary>
public static class PerformanceDateTimeFormatting
{
    /// <summary>§7's list format: "ddd, MMM d · HH:mm", e.g. "Fri, Aug 1 · 20:15".</summary>
    public static string FormatListDateTime(DateTime berlinLocalStartsAt)
    {
        if (berlinLocalStartsAt.Kind != DateTimeKind.Unspecified)
        {
            throw new ArgumentException(
                "Expected a Kind=Unspecified DateTime already converted to Berlin-local time by the caller " +
                $"(got Kind={berlinLocalStartsAt.Kind}).",
                nameof(berlinLocalStartsAt));
        }

        return berlinLocalStartsAt.ToString("ddd, MMM d '·' HH:mm", CultureInfo.InvariantCulture);
    }
}
