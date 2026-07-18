namespace cinescout.core.HallOfFame;

/// <summary>
/// Abstracts the HTTP call to a site's Hall-of-Fame schedule endpoint so the crawl/upsert
/// logic in <see cref="HallOfFameCrawlService"/> can be tested against canned responses.
/// </summary>
public interface IHallOfFameClient
{
    Task<HallOfFameScheduleResponse> GetScheduleAsync(string baseUrl, CancellationToken cancellationToken);
}
