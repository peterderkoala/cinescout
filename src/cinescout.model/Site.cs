namespace cinescout.model;

public class Site
{
    public int Id { get; set; }
    public required string ExternalSiteId { get; set; }
    public required string Name { get; set; }
    public required string CrawlBaseUrl { get; set; }
    public bool IsActive { get; set; }
}
