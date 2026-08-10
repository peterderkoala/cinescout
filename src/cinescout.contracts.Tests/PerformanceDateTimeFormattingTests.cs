using cinescout.contracts;

namespace cinescout.contracts.Tests;

public class PerformanceDateTimeFormattingTests
{
    [Fact]
    public void FormatListDateTime_MatchesSpecExample()
    {
        // §7's own worked example: Fri, Aug 1 · 20:15.
        var berlinLocal = new DateTime(2025, 8, 1, 20, 15, 0, DateTimeKind.Unspecified);

        var formatted = PerformanceDateTimeFormatting.FormatListDateTime(berlinLocal);

        Assert.Equal("Fri, Aug 1 · 20:15", formatted);
    }

    [Fact]
    public void FormatListDateTime_PadsHourAndMinuteButNotDay()
    {
        var berlinLocal = new DateTime(2025, 9, 3, 6, 5, 0, DateTimeKind.Unspecified);

        var formatted = PerformanceDateTimeFormatting.FormatListDateTime(berlinLocal);

        Assert.Equal("Wed, Sep 3 · 06:05", formatted);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void FormatListDateTime_RejectsNonUnspecifiedKind(DateTimeKind kind)
    {
        var value = DateTime.SpecifyKind(new DateTime(2025, 8, 1, 20, 15, 0), kind);

        Assert.Throws<ArgumentException>(() => PerformanceDateTimeFormatting.FormatListDateTime(value));
    }
}
