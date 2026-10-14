namespace cinescout.core.Kinoheld;

/// <summary>Outcome of a single seat-availability fetch attempt for one performance.</summary>
public enum SeatFetchOutcome
{
    /// <summary>A live getSeats call succeeded and its data was persisted.</summary>
    Fetched,

    /// <summary>Cached seat data is within the freshness window; no call was made.</summary>
    Fresh,

    /// <summary>The per-performance cooldown (30s) hasn't elapsed since the last fetch; no call was made.</summary>
    CooldownActive,

    /// <summary>The circuit breaker is tripped (or tripped during this attempt) — all Kinoheld polling is suspended.</summary>
    CircuitOpen,

    /// <summary>Kinoheld answered HTTP 400 — the performance isn't currently bookable. Expected; nothing persisted.</summary>
    NotBookable,

    /// <summary>Kinoheld answered HTTP 404 — the show no longer exists there. Expected; nothing persisted.</summary>
    NotFound,

    /// <summary>The fetch couldn't be attempted at all: unknown performance, or its Cinema has no KinoheldCinemaId yet.</summary>
    Unavailable,
}
