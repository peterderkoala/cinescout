namespace cinescout.model;

/// <summary>
/// Current-state row for one Kinoheld price area for a Performance, upserted each seat crawl —
/// same current-state-mirror posture as SeatStatus. A seat's PriceAreaProviderId resolves against
/// ProviderId here (not Id) — Kinoheld's per-seat "p" field is the price-area *provider* id.
/// </summary>
public class PerformancePriceArea
{
    public int Id { get; set; }
    public int PerformanceId { get; set; }
    public required string ProviderId { get; set; }
    public required string Name { get; set; }
    public decimal OrderPrice { get; set; }
}
