using cinescout.web.Client.Shared;

namespace cinescout.web.Client.Tests;

public sealed class RelativeTimeFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Format_Null_ReturnsNever()
    {
        Assert.Equal("Never", RelativeTimeFormatter.Format(null, Now));
    }

    [Fact]
    public void Format_LessThanAMinuteAgo_ReturnsJustNow()
    {
        Assert.Equal("just now", RelativeTimeFormatter.Format(Now.AddSeconds(-30), Now));
    }

    [Theory]
    [InlineData(1, "1 minute ago")]
    [InlineData(2, "2 minutes ago")]
    [InlineData(59, "59 minutes ago")]
    public void Format_MinutesAgo_IsSingularOrPlural(int minutes, string expected)
    {
        Assert.Equal(expected, RelativeTimeFormatter.Format(Now.AddMinutes(-minutes), Now));
    }

    [Theory]
    [InlineData(1, "1 hour ago")]
    [InlineData(2, "2 hours ago")]
    [InlineData(23, "23 hours ago")]
    public void Format_HoursAgo_IsSingularOrPlural(int hours, string expected)
    {
        Assert.Equal(expected, RelativeTimeFormatter.Format(Now.AddHours(-hours), Now));
    }

    [Theory]
    [InlineData(1, "1 day ago")]
    [InlineData(2, "2 days ago")]
    [InlineData(14, "14 days ago")]
    public void Format_DaysAgo_IsSingularOrPlural(int days, string expected)
    {
        Assert.Equal(expected, RelativeTimeFormatter.Format(Now.AddDays(-days), Now));
    }

    [Fact]
    public void Format_FutureTimestamp_ClampsToJustNow()
    {
        Assert.Equal("just now", RelativeTimeFormatter.Format(Now.AddMinutes(5), Now));
    }
}
