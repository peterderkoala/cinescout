namespace cinescout.model;

/// <summary>
/// Current free/sold state of one seat for one Performance, upserted each Kinoheld crawl.
/// Deliberately excludes Kinoheld's purely cosmetic/rendering fields (pixel position,
/// size, icons) — keeps only what's needed to evaluate a row x seat-number matrix and
/// locate contiguous free blocks via neighbor ids.
/// </summary>
public class SeatStatus
{
    public int Id { get; set; }
    public int PerformanceId { get; set; }

    /// <summary>Kinoheld's own seat id, e.g. "21353011006" — the natural key for adjacency lookups.</summary>
    public required string SourceSeatId { get; set; }
    public required string Row { get; set; }
    public int SeatNumber { get; set; }
    public SeatOccupancyStatus Status { get; set; }
    public string? LeftNeighborSeatId { get; set; }
    public string? RightNeighborSeatId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
