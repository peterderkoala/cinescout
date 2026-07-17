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
}
