using cinescout.web.Client.Api;

namespace cinescout.web.Client.Tests;

public sealed class KinoheldStatusParserTests
{
    [Theory]
    [InlineData("breaker-open", "alert-warning",
        "Live seat data is temporarily unavailable — the Kinoheld connection's circuit breaker is open. Retrying automatically.")]
    [InlineData("not-bookable", "alert-secondary", "This performance isn't currently bookable on Kinoheld.")]
    [InlineData("gone", "alert-secondary", "This performance is no longer available on Kinoheld.")]
    [InlineData("cooldown", "alert-info", "Refreshed too recently — force refresh is available again in 30 seconds.")]
    public void Parse_KnownKinoheldStatus_ReturnsSpecCompliantAlert(string kinoheldStatus, string expectedAlertClass, string expectedMessage)
    {
        var alert = KinoheldStatusParser.Parse(kinoheldStatus);

        Assert.Equal(expectedAlertClass, alert.AlertClass);
        Assert.Equal(expectedMessage, alert.Message);
    }

    [Fact]
    public void Parse_NullKinoheldStatus_ReturnsGenericFallback()
    {
        var alert = KinoheldStatusParser.Parse(null);

        Assert.Equal("alert-danger", alert.AlertClass);
        Assert.Equal("Request failed. Please try again.", alert.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("some-unrecognized-value")]
    public void Parse_UnrecognizedKinoheldStatus_ReturnsGenericFallback(string kinoheldStatus)
    {
        var alert = KinoheldStatusParser.Parse(kinoheldStatus);

        Assert.Equal("alert-danger", alert.AlertClass);
        Assert.Equal("Request failed. Please try again.", alert.Message);
    }
}
