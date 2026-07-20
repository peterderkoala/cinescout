namespace cinescout.model;

public enum MatchStatus
{
    Active,
    Superseded,
}

/// <summary>
/// A record that one Performance has satisfied a WatchedMovie's active preferences at a
/// point in time. A new Active Match is only created the first time a pairing starts
/// satisfying preferences — while Active, re-crawls don't create duplicates.
/// </summary>
public class Match
{
    public int Id { get; set; }
    public int PerformanceId { get; set; }
    public int WatchedMovieId { get; set; }
    public DateTimeOffset MatchedAt { get; set; }
    public MatchStatus Status { get; set; }

    /// <summary>
    /// Whether the applicable FavoriteSeatMatrix's contiguous-block-vs-PartySize check was
    /// satisfied as of the last evaluation. Tracked so a SeatAvailabilityChanged notification
    /// can fire only when this flips, not on every seat-count change.
    /// </summary>
    public bool HasSufficientSeats { get; set; }
}
