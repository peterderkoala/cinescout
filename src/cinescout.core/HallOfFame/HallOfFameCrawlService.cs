using System.Text.Json;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.HallOfFame;

/// <summary>
/// Crawls a single site's Hall-of-Fame schedule and upserts Film/Performance rows, writes an
/// unconditional PerformanceSnapshot per performance per crawl, and flips performances not
/// seen for 2 consecutive crawls to Cancelled.
/// </summary>
public class HallOfFameCrawlService(CineScoutDbContext db, IHallOfFameClient client)
{
    public async Task CrawlSiteAsync(Site site, TimeSpan crawlInterval, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var schedule = await client.GetScheduleAsync(site.CrawlBaseUrl, cancellationToken);

        foreach (var filmDto in schedule.Films)
        {
            var film = await UpsertFilmAsync(site.Id, filmDto, cancellationToken);

            var performanceElements = filmDto.PerformanceGroups
                .SelectMany(group => group.Performances.Values);

            foreach (var performanceElement in performanceElements)
            {
                await UpsertPerformanceAsync(site.Id, film.Id, performanceElement, now, cancellationToken);
            }
        }

        var cancellationCutoff = now - (crawlInterval * 2);
        var missedPerformances = await db.Performances
            .Where(p => p.SiteId == site.Id && p.Status == PerformanceStatus.Normal && p.LastSeenAt <= cancellationCutoff)
            .ToListAsync(cancellationToken);

        foreach (var missed in missedPerformances)
        {
            missed.Status = PerformanceStatus.Cancelled;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Film> UpsertFilmAsync(int siteId, HallOfFameFilmDto filmDto, CancellationToken cancellationToken)
    {
        var externalFilmId = filmDto.DetailId.ToString();

        var film = await db.Films.SingleOrDefaultAsync(
            f => f.SiteId == siteId && f.ExternalFilmId == externalFilmId,
            cancellationToken);

        if (film is null)
        {
            film = new Film
            {
                SiteId = siteId,
                ExternalFilmId = externalFilmId,
                Title = filmDto.FilmTitle,
                PosterUrl = filmDto.PosterUrl,
            };
            db.Films.Add(film);
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            film.Title = filmDto.FilmTitle;
            film.PosterUrl = filmDto.PosterUrl;
        }

        return film;
    }

    private async Task UpsertPerformanceAsync(
        int siteId,
        int filmId,
        JsonElement performanceElement,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var rawPayload = performanceElement.GetRawText();
        var performanceDto = performanceElement.Deserialize<HallOfFamePerformanceDto>()
            ?? throw new InvalidOperationException("Hall-of-Fame performance JSON deserialized to null.");

        var sourcePerformanceId = performanceDto.PerformanceId.ToString();

        var performance = await db.Performances.SingleOrDefaultAsync(
            p => p.SiteId == siteId && p.SourcePerformanceId == sourcePerformanceId,
            cancellationToken);

        var isBookable = performanceDto.IsOnline == 1
            && performanceDto.SaleIsAllowed == 1
            && performanceDto.IsNotBookable == 0;
        var isSoldOut = performanceDto.IsSoldOut == 1;

        if (performance is null)
        {
            performance = new Performance
            {
                FilmId = filmId,
                SiteId = siteId,
                SourcePerformanceId = sourcePerformanceId,
                StartsAt = DateTimeOffset.FromUnixTimeSeconds(performanceDto.UnixDateTime),
                BookingLink = performanceDto.BookingLink,
                Status = PerformanceStatus.Normal,
                IsSoldOut = isSoldOut,
                IsBookable = isBookable,
                LastSeenAt = now,
            };
            db.Performances.Add(performance);
            // PerformanceSnapshot is FK-only (no navigation property), so we need the real
            // generated Id before building the snapshot below — save immediately, matching
            // the Film upsert pattern above.
            await db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            performance.BookingLink = performanceDto.BookingLink;
            performance.IsSoldOut = isSoldOut;
            performance.IsBookable = isBookable;
            performance.Status = PerformanceStatus.Normal;
            performance.LastSeenAt = now;
        }

        // Unconditional snapshot write — every crawl, regardless of whether anything changed.
        // RawPayload is the actual upstream JSON (not a re-serialization of the narrowed DTO
        // above), so the archival record isn't lossy for fields this DTO doesn't happen to model.
        db.PerformanceSnapshots.Add(new PerformanceSnapshot
        {
            PerformanceId = performance.Id,
            CrawledAt = now,
            RawPayload = rawPayload,
        });
    }
}
