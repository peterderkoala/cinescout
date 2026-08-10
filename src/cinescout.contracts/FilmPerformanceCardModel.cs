namespace cinescout.contracts;

/// <summary>
/// The shared card shape backing both <c>FilmPerformanceCard</c> variants (Technical Design Spec
/// §4.1). This is a composed, multi-entity view (title from <c>Film</c>, room from <c>Room</c>,
/// price from the cheapest <c>PerformancePriceArea</c>, status/matchReasons derived per §6.1/§6.2,
/// tracking/isMatch from joins against <c>TrackedMovie</c>/<c>Match</c>) — never Mapperly-generated,
/// per the "composed views stay hand-written" rule.
///
/// §4.1's prop table lists two entries this DTO deliberately omits: <c>variant</c> ("Fed from:
/// caller") and <c>onSelect</c> ("Fed from: navigate to Performance Detail"). Neither is server
/// data — <c>variant</c> is a rendering choice the calling page/section makes (e.g. a compact list
/// vs. a featured section), and <c>onSelect</c> is a UI callback/delegate that cannot cross the
/// wire as JSON. Both remain <c>FilmPerformanceCard.razor</c> parameters set at the call site
/// (issue #77), not part of this payload.
/// </summary>
public sealed record FilmPerformanceCardModel
{
    /// <summary>Film.Title.</summary>
    public required string Title { get; init; }

    /// <summary>Cinema.Name — featured variant only per §4.1; left null for compact-only callers.</summary>
    public string? Cinema { get; init; }

    /// <summary>Room.Name.</summary>
    public required string Room { get; init; }

    /// <summary>
    /// Performance.StartsAt, pre-formatted server-side per §7's list format (e.g. "Fri, Aug 1 ·
    /// 20:15") via <see cref="PerformanceDateTimeFormatting.FormatListDateTime"/>. A display string,
    /// not a raw timestamp — §4.1 types this prop as <c>string</c>.
    /// </summary>
    public required string DateTime { get; init; }

    /// <summary>
    /// Cheapest PerformancePriceArea.OrderPrice for the performance, currency-formatted; null if no
    /// price areas were crawled yet (§9.4 gap #2 — show nothing rather than a placeholder).
    /// </summary>
    public string? Price { get; init; }

    /// <summary>Derived per §6.1.</summary>
    public required PerformanceCardStatus Status { get; init; }

    /// <summary>Whether a TrackedMovie row exists for Performance.FilmId.</summary>
    public required bool Tracking { get; init; }

    /// <summary>Whether an Active Match exists for this performance.</summary>
    public required bool IsMatch { get; init; }

    /// <summary>Derived per §6.2, e.g. "3 seats free", "Fri evening". Empty when there are none.</summary>
    public required IReadOnlyList<string> MatchReasons { get; init; }

    /// <summary>
    /// Film.PosterUrl; null falls back to the centered bi-film placeholder tile client-side (§9.4
    /// gap #3).
    /// </summary>
    public string? PosterUrl { get; init; }

    /// <summary>Performance.BookingLink.</summary>
    public required string BookingLink { get; init; }
}
