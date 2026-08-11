using System.Text.Json;
using cinescout.core.Discord;
using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.HallOfFame;

/// <summary>
/// Crawls a single cinema's Hall-of-Fame schedule and upserts Film/Performance rows, writes an
/// unconditional PerformanceSnapshot per performance per crawl, and flips performances not
/// seen for 2 consecutive crawls to Cancelled. Sends a NewFilmAdded notification the first time a
/// film is ever seen at a cinema — deliberately unfiltered and un-gated against a cinema's first-ever
/// crawl (accepted, one-time cost rather than new domain concepts; see issue #43).
/// </summary>
public class HallOfFameCrawlService(CineScoutDbContext db, IHallOfFameClient client, IDiscordNotifier notifier)
{
    public async Task CrawlCinemaAsync(Cinema cinema, TimeSpan crawlInterval, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var schedule = await client.GetScheduleAsync(cinema.CrawlBaseUrl, cancellationToken);

        // Films with a null detailId have no stable external id to upsert against (see #59) —
        // skip them rather than crash the whole crawl on them.
        foreach (var filmDto in schedule.Films.Where(f => f.DetailId is not null))
        {
            var film = await UpsertFilmAsync(cinema.Id, filmDto, cancellationToken);

            var performanceElements = filmDto.PerformanceGroups
                .SelectMany(group => group.Performances.Values);

            foreach (var performanceElement in performanceElements)
            {
                await UpsertPerformanceAsync(cinema.Id, film.Id, performanceElement, now, cancellationToken);
            }
        }

        var cancellationCutoff = now - (crawlInterval * 2);
        var missedPerformances = await db.Performances
            .Where(p => p.CinemaId == cinema.Id && p.Status == PerformanceStatus.Normal && p.LastSeenAt <= cancellationCutoff)
            .ToListAsync(cancellationToken);

        foreach (var missed in missedPerformances)
        {
            missed.Status = PerformanceStatus.Cancelled;
        }

        // Stamped only once every step above has completed without throwing — not before the
        // GetScheduleAsync call, and not mid-loop (the film/performance upserts below each call
        // SaveChangesAsync themselves, which would otherwise flush this early and durably record a
        // "successful" crawl even if a later film/performance in the same cycle then threw and
        // aborted the rest). Never derived from PerformanceSnapshot (ADR 0003) — answers "is
        // crawling working for this cinema?" directly, independent of whether the response
        // happened to contain any films.
        cinema.LastCrawlAt = now;

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Film> UpsertFilmAsync(int cinemaId, HallOfFameFilmDto filmDto, CancellationToken cancellationToken)
    {
        var externalFilmId = filmDto.DetailId!.Value.ToString();

        var film = await db.Films.SingleOrDefaultAsync(
            f => f.CinemaId == cinemaId && f.ExternalFilmId == externalFilmId,
            cancellationToken);

        if (film is null)
        {
            film = new Film
            {
                CinemaId = cinemaId,
                ExternalFilmId = externalFilmId,
                Title = filmDto.FilmTitle,
                PosterUrl = filmDto.PosterUrl,
            };
            db.Films.Add(film);
            await db.SaveChangesAsync(cancellationToken);

            await NotifyNewFilmAsync(film, cancellationToken);
        }
        else
        {
            film.Title = filmDto.FilmTitle;
            film.PosterUrl = filmDto.PosterUrl;
        }

        return film;
    }

    private Task NotifyNewFilmAsync(Film film, CancellationToken cancellationToken) =>
        NotificationDispatcher.SendAndLogAsync(
            db, notifier, NotificationType.NewFilmAdded, matchId: null, $"New film added: **{film.Title}**.", cancellationToken);

    private async Task UpsertPerformanceAsync(
        int cinemaId,
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
            p => p.CinemaId == cinemaId && p.SourcePerformanceId == sourcePerformanceId,
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
                CinemaId = cinemaId,
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
