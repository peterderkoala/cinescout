using System.Globalization;
using cinescout.model;

namespace cinescout.core.Discord;

/// <summary>Pure builders, one per notification type (#139's embed table). Start times render via Discord's <c>&lt;t:…&gt;</c> markup, so they show in the viewer's time zone.</summary>
public static class NotificationMessages
{
    public static NotificationMessage RulesMatched(
        Film film, Cinema cinema, Room room, Performance performance, FavoriteTimeWindow window,
        FavoriteSeatMatrix matrix, int freeSeats, decimal? cheapestPrice, DateTimeOffset now)
    {
        List<NotificationField> fields =
        [
            .. ScreeningFields(cinema, room, performance),
            new("Time window", $"{window.DaysOfWeek} {window.StartTime:HH:mm}–{window.EndTime:HH:mm}"),
            new("Seats", $"{freeSeats} adjacent free in \"{matrix.Name}\", needs {matrix.PartySize}"),
        ];
        if (cheapestPrice is decimal price)
        {
            fields.Add(new("From", $"{price.ToString("0.00", CultureInfo.InvariantCulture)} €"));
        }

        return Message("🎬 Matches your preferences", film, 0x2ECC71, fields, performance.BookingLink, now);
    }

    public static NotificationMessage SeatAvailabilityChanged(
        Film film, Cinema cinema, Room room, Performance performance, bool hasSufficientSeats, FavoriteSeatMatrix? matrix, DateTimeOffset now)
    {
        List<NotificationField> fields = [.. ScreeningFields(cinema, room, performance)];
        if (!hasSufficientSeats)
        {
            return Message("🔕 No longer enough seats", film, 0xE67E22, fields, performance.BookingLink, now);
        }

        if (matrix is not null)
        {
            fields.Add(new("Seats", $"Needs {matrix.PartySize} adjacent in \"{matrix.Name}\""));
        }

        return Message("🔔 Seats available again", film, 0x1ABC9C, fields, performance.BookingLink, now);
    }

    public static NotificationMessage TrackStarted(Film film, Cinema cinema, DateTimeOffset now) =>
        Message("👀 Now tracking", film, 0x3498DB, [new("Cinema", cinema.Name)], url: null, now,
            "You'll be notified when a screening matches your preferences.");

    public static NotificationMessage TrackStopped(Film film, Cinema cinema, DateTimeOffset now) =>
        Message("⏹️ Stopped tracking", film, 0x95A5A6, [new("Cinema", cinema.Name)], url: null, now);

    public static NotificationMessage NewFilmAdded(Film film, Cinema cinema, DateTimeOffset now) =>
        Message("🆕 New film", film, 0x9B59B6, [new("Cinema", cinema.Name)], url: null, now);

    private static NotificationField[] ScreeningFields(Cinema cinema, Room room, Performance performance)
    {
        var unix = performance.StartsAt.ToUnixTimeSeconds();
        return
        [
            new("Cinema", cinema.Name, Inline: true),
            new("Room", room.Name, Inline: true),
            new("Starts", $"<t:{unix}:F> (<t:{unix}:R>)", Inline: true),
        ];
    }

    private static NotificationMessage Message(
        string title, Film film, int color, IReadOnlyList<NotificationField> fields, string? url, DateTimeOffset now, string? detail = null) =>
        new(title, detail is null ? $"**{film.Title}**" : $"**{film.Title}**\n{detail}", color, fields, url, film.PosterUrl, now);
}
