using System.Net;
using System.Runtime.CompilerServices;
using cinescout.core.Kinoheld;

namespace cinescout.core.Tests;

public class KinoheldClientTests
{
    /// <summary>
    /// Real, live-fetched sample of a Kinoheld widget page (HALL OF FAME Kamp-Lintfort),
    /// captured verbatim for the kind of parsing this client needs to do — see
    /// docs/api/kinoheld/seating-api-research.md (branch research/kinoheld-seating-api) for the
    /// original research this format is documented from.
    /// </summary>
    private static string FixturePath([CallerFilePath] string sourceFilePath = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFilePath)!, "Fixtures", "kinoheld-widget-page.html");

    [Fact]
    public async Task Parses_auditoriums_from_real_captured_widget_page()
    {
        var html = await File.ReadAllTextAsync(FixturePath());

        var handler = new StubHttpMessageHandler(html);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.kinoheld.de") };
        var client = new KinoheldClient(httpClient);

        var config = await client.GetWidgetConfigAsync(
            "https://www.kinoheld.de/kino-kamp-lintfort/hall-of-fame?mode=widget&change=no&showId=74011",
            CancellationToken.None);

        Assert.Equal(7, config.Auditoriums.Count);
        Assert.Equal(
            [
                ("8255", "Kino 1"),
                ("8257", "Kino 2"),
                ("8259", "Kino 3"),
                ("8261", "Kino 4"),
                ("8263", "Kino 5"),
                ("8265", "Kino 6"),
                ("8267", "Kino 7"),
            ],
            config.Auditoriums.Select(a => (a.Id, a.Name)));
    }

    private sealed class StubHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            });
    }
}
