using cinescout.core.Discord;
using cinescout.model;

namespace cinescout.core.Tests;

/// <summary>Pure: the per-type embed content decided in #139, no I/O.</summary>
public class NotificationMessagesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);

    // 2026-10-24 18:15 UTC = 20:15 Berlin; unix 1792865700.
    private static readonly Performance Performance = new()
    {
        Id = 7, FilmId = 1, CinemaId = 1, RoomId = 3, SourcePerformanceId = "p1",
        StartsAt = new DateTimeOffset(2026, 10, 24, 18, 15, 0, TimeSpan.Zero), BookingLink = "https://kinoheld.de/book/7",
    };

    private static readonly Film Film = new() { Id = 1, CinemaId = 1, ExternalFilmId = "f1", Title = "Vaiana", PosterUrl = "https://img.example/vaiana.jpg" };
    private static readonly Cinema Cinema = new() { Id = 1, ExternalCinemaId = "580", Name = "Hall of Fame", CrawlBaseUrl = "https://x" };
    private static readonly Room Room = new() { Id = 3, CinemaId = 1, ExternalAuditoriumId = "a", Name = "Saal 1" };

    private static readonly FavoriteSeatMatrix Matrix = new()
    {
        Name = "Parkett", RowStart = "E", RowEnd = "H", SeatNumberStart = 1, SeatNumberEnd = 20, PartySize = 2,
    };

    private static readonly FavoriteTimeWindow Window = new()
    {
        DaysOfWeek = DaysOfWeekFlags.Friday | DaysOfWeekFlags.Saturday, StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(23, 0),
    };

    private static void AssertShell(NotificationMessage message, string title, int color)
    {
        Assert.Equal(title, message.Title);
        Assert.Equal("**Vaiana**", message.Description.Split('\n')[0]);
        Assert.Equal(color, message.Color);
        Assert.Equal("https://img.example/vaiana.jpg", message.ThumbnailUrl);
        Assert.Equal("CineScout", message.Footer);
        Assert.Equal(Now, message.Timestamp);
    }

    [Fact]
    public void RulesMatched_carries_cinema_room_start_window_seats_and_price_and_links_the_booking()
    {
        var message = NotificationMessages.RulesMatched(Film, Cinema, Room, Performance, Window, Matrix, freeSeats: 4, cheapestPrice: 12.5m, Now);

        AssertShell(message, "🎬 Matches your preferences", 0x2ECC71);
        Assert.Equal("https://kinoheld.de/book/7", message.Url);
        Assert.Equal(
            [
                new NotificationField("Cinema", "Hall of Fame", Inline: true),
                new NotificationField("Room", "Saal 1", Inline: true),
                new NotificationField("Starts", "<t:1792865700:F> (<t:1792865700:R>)", Inline: true),
                new NotificationField("Time window", "Friday, Saturday 18:00–23:00"),
                new NotificationField("Seats", "4 adjacent free in \"Parkett\", needs 2"),
                new NotificationField("From", "12.50 €"),
            ],
            message.Fields);
    }

    [Fact]
    public void RulesMatched_without_a_known_price_has_no_From_field() =>
        Assert.DoesNotContain(
            NotificationMessages.RulesMatched(Film, Cinema, Room, Performance, Window, Matrix, freeSeats: 2, cheapestPrice: null, Now).Fields,
            f => f.Name == "From");

    [Fact]
    public void SeatAvailabilityChanged_seats_back_is_teal_with_the_party_size()
    {
        var message = NotificationMessages.SeatAvailabilityChanged(Film, Cinema, Room, Performance, hasSufficientSeats: true, Matrix, Now);

        AssertShell(message, "🔔 Seats available again", 0x1ABC9C);
        Assert.Equal("https://kinoheld.de/book/7", message.Url);
        Assert.Equal(["Cinema", "Room", "Starts", "Seats"], message.Fields.Select(f => f.Name));
        Assert.Equal("Needs 2 adjacent in \"Parkett\"", message.Fields[^1].Value);
    }

    [Fact]
    public void SeatAvailabilityChanged_seats_gone_is_orange_without_a_seats_field()
    {
        var message = NotificationMessages.SeatAvailabilityChanged(Film, Cinema, Room, Performance, hasSufficientSeats: false, Matrix, Now);

        AssertShell(message, "🔕 No longer enough seats", 0xE67E22);
        Assert.Equal(["Cinema", "Room", "Starts"], message.Fields.Select(f => f.Name));
    }

    [Fact]
    public void TrackStarted_explains_what_happens_next()
    {
        var message = NotificationMessages.TrackStarted(Film, Cinema, Now);

        AssertShell(message, "👀 Now tracking", 0x3498DB);
        Assert.Equal("**Vaiana**\nYou'll be notified when a screening matches your preferences.", message.Description);
        Assert.Null(message.Url);
        Assert.Equal([new NotificationField("Cinema", "Hall of Fame")], message.Fields);
    }

    [Fact]
    public void TrackStopped_and_NewFilmAdded_are_the_shell_plus_cinema()
    {
        var stopped = NotificationMessages.TrackStopped(Film, Cinema, Now);
        var added = NotificationMessages.NewFilmAdded(Film, Cinema, Now);

        AssertShell(stopped, "⏹️ Stopped tracking", 0x95A5A6);
        AssertShell(added, "🆕 New film", 0x9B59B6);
        Assert.Equal([new NotificationField("Cinema", "Hall of Fame")], stopped.Fields);
        Assert.Equal([new NotificationField("Cinema", "Hall of Fame")], added.Fields);
        Assert.Null(added.Url);
    }

    [Fact]
    public void A_film_without_a_poster_has_no_thumbnail() =>
        Assert.Null(NotificationMessages.NewFilmAdded(new Film { CinemaId = 1, ExternalFilmId = "x", Title = "Vaiana" }, Cinema, Now).ThumbnailUrl);
}
