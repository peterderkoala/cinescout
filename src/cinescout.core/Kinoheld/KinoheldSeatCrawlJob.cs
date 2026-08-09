namespace cinescout.core.Kinoheld;

/// <summary>
/// Hangfire recurring-job entry point: crawls Kinoheld seat availability for all upcoming
/// performances of currently-tracked films.
/// </summary>
public class KinoheldSeatCrawlJob(KinoheldSeatCrawlService crawlService)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await crawlService.CrawlTrackedAsync(cancellationToken);
    }
}
