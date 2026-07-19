using cinescout.model;
using cinescout.persistence;
using Microsoft.EntityFrameworkCore;

namespace cinescout.core.Kinoheld;

/// <summary>
/// Seeds/upserts <see cref="Room"/> rows for a <see cref="Site"/> from its Kinoheld widget
/// config's auditorium list. Deliberately split out from the ToS-sensitive seat-level crawler
/// (#8) so the preferences UI can get a room picker without needing that crawler built yet.
///
/// Room seeding is eager, not lazy: run once per Site (e.g. at app startup), independent of any
/// regular seat crawl. It derives the Kinoheld widget URL to fetch from any already-crawled
/// <see cref="Performance.BookingLink"/> for the site (any bookingLink 301-redirects to the same
/// cinema-wide widget page), rather than from a dedicated Site field — if nothing has been
/// crawled yet for a site, seeding is a safe no-op that will pick up once a Hall-of-Fame crawl
/// has run.
/// </summary>
public sealed class KinoheldRoomSeedingService(CineScoutDbContext db, IKinoheldClient client)
{
    public async Task SeedRoomsForSiteAsync(Site site, CancellationToken cancellationToken)
    {
        var bookingLink = await db.Performances
            .Where(p => p.SiteId == site.Id)
            .OrderByDescending(p => p.StartsAt)
            .Select(p => p.BookingLink)
            .FirstOrDefaultAsync(cancellationToken);

        if (bookingLink is null)
        {
            return; // nothing crawled yet for this site to derive a widget URL from — safe no-op, will pick up once Hall-of-Fame crawl has run
        }

        var config = await client.GetWidgetConfigAsync(bookingLink, cancellationToken);

        // Capture Kinoheld's numeric cinema id alongside the auditoriums — the seat crawl (#22)
        // needs it as "cid" and never guesses it.
        if (site.KinoheldCinemaId != config.CinemaId)
        {
            site.KinoheldCinemaId = config.CinemaId;
        }

        foreach (var auditorium in config.Auditoriums)
        {
            var room = await db.Rooms.SingleOrDefaultAsync(
                r => r.SiteId == site.Id && r.ExternalAuditoriumId == auditorium.Id, cancellationToken);

            if (room is null)
            {
                db.Rooms.Add(new Room { SiteId = site.Id, ExternalAuditoriumId = auditorium.Id, Name = auditorium.Name });
            }
            else if (room.Name != auditorium.Name)
            {
                room.Name = auditorium.Name;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
