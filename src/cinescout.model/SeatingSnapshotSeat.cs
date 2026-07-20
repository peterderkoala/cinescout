namespace cinescout.model;

/// <summary>Append-only history detail: one row per seat per SeatingSnapshot.</summary>
public class SeatingSnapshotSeat
{
    public int Id { get; set; }
    public int SnapshotId { get; set; }
    public required string SourceSeatId { get; set; }
    public required string Row { get; set; }
    public int SeatNumber { get; set; }
    public SeatOccupancyStatus Status { get; set; }
    public string? LeftNeighborSeatId { get; set; }
    public string? RightNeighborSeatId { get; set; }
    public string? PriceAreaProviderId { get; set; }
}
