namespace cinescout.web.Client.Api;

/// <summary>One Bootstrap alert class + its exact copy, per Technical Design Spec.md §5.7.</summary>
public sealed record KinoheldStatusAlert(string AlertClass, string Message);

/// <summary>
/// Maps the <c>kinoheldStatus</c> <c>ProblemDetails</c> extension member (issue #83's resolution:
/// <c>"breaker-open" | "not-bookable" | "gone" | "cooldown"</c>) to §5.7's exact alert-class/copy
/// table. Every screen ticket that surfaces a Kinoheld-backed error routes through this instead of
/// duplicating the table — anything without a recognized value (including a response with no
/// <c>kinoheldStatus</c> member at all, e.g. a plain validation or 500 error) falls through to a
/// generic fallback, per #90's "What to build" section.
///
/// Deliberately takes the already-extracted extension value, not a raw HTTP response or JSON
/// document — reading the response body is I/O the caller owns; this class is pure mapping logic,
/// per #81's testing convention (push logic into pure, directly-testable functions).
/// </summary>
public static class KinoheldStatusParser
{
    private static readonly KinoheldStatusAlert Fallback = new(
        "alert-danger",
        "Request failed. Please try again.");

    public static KinoheldStatusAlert Parse(string? kinoheldStatus) => kinoheldStatus switch
    {
        "breaker-open" => new KinoheldStatusAlert(
            "alert-warning",
            "Live seat data is temporarily unavailable — the Kinoheld connection's circuit breaker is open. Retrying automatically."),
        "not-bookable" => new KinoheldStatusAlert(
            "alert-secondary",
            "This performance isn't currently bookable on Kinoheld."),
        "gone" => new KinoheldStatusAlert(
            "alert-secondary",
            "This performance is no longer available on Kinoheld."),
        "cooldown" => new KinoheldStatusAlert(
            "alert-info",
            "Refreshed too recently — force refresh is available again in 30 seconds."),
        _ => Fallback,
    };
}
