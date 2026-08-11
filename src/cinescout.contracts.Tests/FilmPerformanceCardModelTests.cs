using cinescout.contracts;

namespace cinescout.contracts.Tests;

/// <summary>
/// No behaviour to unit test on a plain data shape — this pins the §4.1 prop table's required vs.
/// optional split (compile-time, via `required`) against real sample values, so a future edit that
/// accidentally drops a prop or loosens its nullability is caught here.
/// </summary>
public class FilmPerformanceCardModelTests
{
    [Fact]
    public void Constructs_FeaturedMatchCard_WithAllOptionalFieldsPresent()
    {
        var model = new FilmPerformanceCardModel
        {
            PerformanceId = 42,
            Title = "Perfect Days",
            Cinema = "Kino am Rathaus",
            Room = "Saal 2",
            DateTime = "Fri, Aug 1 · 20:15",
            Price = "€9.50",
            Status = PerformanceCardStatus.Available,
            Tracking = true,
            IsMatch = true,
            MatchReasons = ["3 seats free", "Fri evening"],
            PosterUrl = "https://example.invalid/poster.jpg",
            BookingLink = "https://kinoheld.de/book/123",
        };

        Assert.Equal("Perfect Days", model.Title);
        Assert.Equal("Kino am Rathaus", model.Cinema);
        Assert.Equal("Saal 2", model.Room);
        Assert.Equal("Fri, Aug 1 · 20:15", model.DateTime);
        Assert.Equal("€9.50", model.Price);
        Assert.Equal(PerformanceCardStatus.Available, model.Status);
        Assert.True(model.Tracking);
        Assert.True(model.IsMatch);
        Assert.Equal(["3 seats free", "Fri evening"], model.MatchReasons);
        Assert.Equal("https://example.invalid/poster.jpg", model.PosterUrl);
        Assert.Equal("https://kinoheld.de/book/123", model.BookingLink);
    }

    [Fact]
    public void Constructs_CompactCard_WithOptionalFieldsOmitted()
    {
        // Compact rows don't show cinema, may have no crawled price yet, and no poster.
        var model = new FilmPerformanceCardModel
        {
            PerformanceId = 42,
            Title = "Perfect Days",
            Room = "Saal 2",
            DateTime = "Fri, Aug 1 · 20:15",
            Status = PerformanceCardStatus.None,
            Tracking = false,
            IsMatch = false,
            MatchReasons = [],
            BookingLink = "https://kinoheld.de/book/123",
        };

        Assert.Null(model.Cinema);
        Assert.Null(model.Price);
        Assert.Null(model.PosterUrl);
        Assert.Empty(model.MatchReasons);
    }
}
