namespace cinescout.core.Kinoheld;

/// <summary>
/// Hangfire recurring-job entry point: crawls Kinoheld seat availability for all upcoming
/// performances of currently-watched films.
/// </summary>
public class KinoheldSeatCrawlJob(KinoheldSeatCrawlService crawlService)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await crawlService.CrawlWatchedAsync(cancellationToken);
    }
}
