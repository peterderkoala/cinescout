namespace cinescout.model;

/// <summary>
/// A record that one Performance has satisfied a TrackedMovie's active preferences at a
/// point in time. Created the first time a pairing starts satisfying preferences and never
/// closed, so re-crawls don't create duplicates; HasSufficientSeats is its live state.
/// </summary>
public class Match
{
    public int Id { get; set; }
    public int PerformanceId { get; set; }
    public int TrackedMovieId { get; set; }
    public DateTimeOffset MatchedAt { get; set; }

    /// <summary>
    /// Whether the applicable FavoriteSeatMatrix's contiguous-block-vs-PartySize check was
    /// satisfied as of the last evaluation. Tracked so a SeatAvailabilityChanged notification
    /// can fire only when this flips, not on every seat-count change.
    /// </summary>
    public bool HasSufficientSeats { get; set; }
}
