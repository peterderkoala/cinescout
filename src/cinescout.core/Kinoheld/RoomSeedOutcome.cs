namespace cinescout.core.Kinoheld;

/// <summary>Outcome of a single room-seeding attempt for one cinema — the room-seeding analog of <see cref="SeatFetchOutcome"/>.</summary>
public enum RoomSeedOutcome
{
    /// <summary>A widget-config fetch succeeded and Room rows (plus Cinema.KinoheldCinemaId) were upserted.</summary>
    Seeded,

    /// <summary>Nothing has been crawled yet for this cinema to derive a widget URL from; safe no-op.</summary>
    Unavailable,

    /// <summary>The circuit breaker is tripped (or tripped during this attempt) — all Kinoheld polling is suspended.</summary>
    CircuitOpen,
}
