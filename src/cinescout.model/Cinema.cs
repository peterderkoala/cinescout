namespace cinescout.model;

public class Cinema
{
    public int Id { get; set; }
    public required string ExternalCinemaId { get; set; }
    public required string Name { get; set; }
    public required string CrawlBaseUrl { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Kinoheld's numeric cinema id (dataLayer cinema.id), captured during room seeding; needed as "cid" by the seat-availability fetch. Null until the first successful widget-config fetch.</summary>
    public string? KinoheldCinemaId { get; set; }

    /// <summary>
    /// Stamped by HallOfFameCrawlService.CrawlCinemaAsync only, immediately after
    /// IHallOfFameClient.GetScheduleAsync returns without throwing — never derived from
    /// PerformanceSnapshot (see ADR 0003). Null means this cinema has never been successfully
    /// crawled.
    /// </summary>
    public DateTimeOffset? LastCrawlAt { get; set; }
}
