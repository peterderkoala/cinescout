namespace cinescout.model;

/// <summary>
/// Append-only history: one row per Hall-of-Fame crawl per performance, written
/// unconditionally regardless of whether anything changed.
/// </summary>
public class PerformanceSnapshot
{
    public int Id { get; set; }
    public int PerformanceId { get; set; }
    public DateTimeOffset CrawledAt { get; set; }
    public required string RawPayload { get; set; }
}
