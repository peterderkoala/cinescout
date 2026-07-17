namespace cinescout.model;

/// <summary>
/// Append-only history header: one row per Kinoheld crawl per performance, with the raw
/// response payload retained.
/// </summary>
public class SeatingSnapshot
{
    public int Id { get; set; }
    public int PerformanceId { get; set; }
    public DateTimeOffset CrawledAt { get; set; }
    public required string RawPayload { get; set; }
}
