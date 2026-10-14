using cinescout.web.Client.Api;

namespace cinescout.web.Client.Tests;

public sealed class ApiProblemTests
{
    [Theory]
    [InlineData("Title", "Detail", "Detail")]
    [InlineData("Title", null, "Title")]
    [InlineData(null, null, "Save failed.")]
    public void MessageOr_PrefersDetailThenTitleThenFallback(string? title, string? detail, string expected) =>
        Assert.Equal(expected, new ApiProblem(title, detail, null).MessageOr("Save failed."));
}
