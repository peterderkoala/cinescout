using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace cinescout.core.HallOfFame;

/// <summary>
/// Hangfire recurring-job entry point: crawls every active Cinema's Hall-of-Fame schedule.
/// </summary>
public class HallOfFameCrawlJob(CineScoutDbContext db, HallOfFameCrawlService crawlService, IConfiguration configuration, ILogger<HallOfFameCrawlJob> logger)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var crawlIntervalHours = configuration.GetValue("HallOfFame:CrawlIntervalHours", 1);
        var crawlInterval = TimeSpan.FromHours(crawlIntervalHours);
        var now = DateTimeOffset.UtcNow;

        var activeCinemas = await db.Cinemas.Where(s => s.IsActive).ToListAsync(cancellationToken);

        foreach (var cinema in activeCinemas)
        {
            try
            {
                await crawlService.CrawlCinemaAsync(cinema, crawlInterval, now, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One cinema's failure must not skip every cinema that sorts after it this cycle
                // (ADR 0003) — log and continue rather than let the foreach abort.
                logger.LogError(ex, "Hall-of-Fame crawl failed for Cinema {CinemaId} ({CinemaName}); continuing with the rest of this cycle.", cinema.Id, cinema.Name);
            }
        }
    }
}
