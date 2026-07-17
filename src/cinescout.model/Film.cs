namespace cinescout.model;

/// <summary>
/// Scoped per site listing (mirrors the source API's own scoping), not a cross-site
/// canonical movie identity — the same movie at two sites is two Film rows.
/// </summary>
public class Film
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public required string ExternalFilmId { get; set; }
    public required string Title { get; set; }
}
