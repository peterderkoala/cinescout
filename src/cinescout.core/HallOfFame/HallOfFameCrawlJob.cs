using cinescout.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace cinescout.core.HallOfFame;

/// <summary>
/// Hangfire recurring-job entry point: crawls every active Site's Hall-of-Fame schedule.
/// </summary>
public class HallOfFameCrawlJob(CineScoutDbContext db, HallOfFameCrawlService crawlService, IConfiguration configuration)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var crawlIntervalHours = configuration.GetValue("HallOfFame:CrawlIntervalHours", 1);
        var crawlInterval = TimeSpan.FromHours(crawlIntervalHours);
        var now = DateTimeOffset.UtcNow;

        var activeSites = await db.Sites.Where(s => s.IsActive).ToListAsync(cancellationToken);

        foreach (var site in activeSites)
        {
            await crawlService.CrawlSiteAsync(site, crawlInterval, now, cancellationToken);
        }
    }
}
