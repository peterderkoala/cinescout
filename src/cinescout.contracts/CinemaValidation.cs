namespace cinescout.contracts;

/// <summary>
/// Required-field + URL-format checks only, no live "test connection" call (ADR 0005). Lives here,
/// not duplicated in both cinescout.web's endpoint and cinescout.web.Client's Cinemas.razor, since
/// both projects already reference this leaf — a second hand-copied implementation would risk the
/// two silently drifting (e.g. one side gaining a max-length check the other doesn't).
/// </summary>
public static class CinemaValidation
{
    public static string? Validate(CinemaWriteRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.ExternalCinemaId))
        {
            return "External cinema id is required.";
        }

        if (string.IsNullOrWhiteSpace(request.CrawlBaseUrl)
            || !Uri.TryCreate(request.CrawlBaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            return "Crawl base URL must be a valid http(s) URL.";
        }

        return null;
    }
}
