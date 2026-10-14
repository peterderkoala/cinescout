namespace cinescout.model;

/// <summary>
/// Scoped per cinema listing (mirrors the source API's own scoping), not a cross-cinema
/// canonical movie identity — the same movie at two cinemas is two Film rows.
/// </summary>
public class Film
{
    public int Id { get; set; }
    public int CinemaId { get; set; }
    public required string ExternalFilmId { get; set; }
    public required string Title { get; set; }

    /// <summary>Poster image URL from the Hall-of-Fame crawl (its "posterUrl" field); null until crawled.</summary>
    public string? PosterUrl { get; set; }
}
