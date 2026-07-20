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

        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, html);
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

    [Fact]
    public async Task Parses_cinema_id_from_real_captured_widget_page()
    {
        var html = await File.ReadAllTextAsync(FixturePath());

        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, html);
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://www.kinoheld.de") };
        var client = new KinoheldClient(httpClient);

        var config = await client.GetWidgetConfigAsync(
            "https://www.kinoheld.de/kino-kamp-lintfort/hall-of-fame?mode=widget&change=no&showId=74011",
            CancellationToken.None);

        // cinema.id (numeric, stringified) — NOT the opaque cinema.cid string ("MjU4NzYyMA"),
        // which is a different, GraphQL-facing identifier also present in the same blob.
        Assert.Equal("2135", config.CinemaId);
    }

    /// <summary>
    /// Canned 200 body built from the live-verified shape documented in
    /// docs/api/kinoheld/seating-api-research.md: "seats" is a JSON OBJECT keyed by seat id,
    /// "n" is a string, "sl"/"sr" are a string id or the NUMBER 0, plus cosmetic fields the
    /// parser must ignore.
    /// </summary>
    private const string SuccessBody =
        """
        {
          "seats": {
            "21353011006": {"x":31.2,"y":180,"w":"24","h":"24","r":"D","n":"1","sl":0,"sr":"21353011007","status":"sf","p":"1","areaName":"Komfort","secId":"8259","fnss":false},
            "21353011007": {"x":55.2,"y":180,"w":"24","h":"24","r":"D","n":"2","sl":"21353011006","sr":0,"status":"ss","p":"1","areaName":"Komfort","secId":"8259","fnss":false},
            "21353011010": {"x":31.2,"y":210,"w":"24","h":"24","r":"E","n":"1","sl":0,"sr":0,"status":"sn","p":"1","areaName":"Komfort","secId":"8259","fnss":false}
          },
          "sectors": [
            {"id":"8259","hasSeatSelection":true,"availableSeats":{"order":true,"reservation":true},"seatsStats":{"total":3,"free":1}}
          ],
          "seat_selection_available": true,
          "priceAreas": [
            {
              "id": "67711089", "providerId": "1", "name": "Komfort",
              "color": "#ffffff", "isActive": true, "seatSelectionAvailable": true,
              "orderPrice": "15.3636",
              "categories": [
                {"id": "203003403", "name": "Normal", "displayName": "Normal", "val": 15.3636, "valWithFee": 16.8999, "min": 0, "max": 10, "step": 1}
              ]
            }
          ]
        }
        """;

    private static (KinoheldClient Client, RecordingStubHttpMessageHandler Handler) CreateSeatsClient(HttpStatusCode statusCode, string body)
    {
        var handler = new RecordingStubHttpMessageHandler(statusCode, body);
        var httpClient = new HttpClient(handler);
        return (new KinoheldClient(httpClient), handler);
    }

    [Fact]
    public async Task GetSeats_sends_the_exact_widget_form_body()
    {
        var (client, handler) = CreateSeatsClient(HttpStatusCode.OK, SuccessBody);

        await client.GetSeatsAsync("2135", "74705", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.LastRequestMethod);
        Assert.Equal("https://www.kinoheld.de/ajax/getSeats", handler.LastRequestUri?.ToString());
        Assert.Equal("cid=2135&showId=74705&mode=widget", handler.LastRequestBody);
    }

    [Fact]
    public async Task GetSeats_200_parses_seats_including_mixed_neighbor_types_and_statuses()
    {
        var (client, _) = CreateSeatsClient(HttpStatusCode.OK, SuccessBody);

        var result = await client.GetSeatsAsync("2135", "74705", CancellationToken.None);

        var success = Assert.IsType<KinoheldSeatsResult.Success>(result);
        Assert.Equal(SuccessBody, success.RawPayload);
        Assert.Equal(3, success.Seats.Count);

        var first = success.Seats.Single(s => s.SourceSeatId == "21353011006");
        Assert.Equal("D", first.Row);
        Assert.Equal(1, first.SeatNumber);
        Assert.Equal("sf", first.RawStatus);
        Assert.Null(first.LeftNeighborSeatId); // sl: 0 (JSON number) means no neighbor
        Assert.Equal("21353011007", first.RightNeighborSeatId); // sr: string id
        Assert.Equal("8259", first.SectorId);
        Assert.Equal("1", first.PriceAreaProviderId);

        var priceArea = Assert.Single(success.PriceAreas);
        Assert.Equal("67711089", priceArea.Id);
        Assert.Equal("1", priceArea.ProviderId);
        Assert.Equal("Komfort", priceArea.Name);
        Assert.Equal(15.3636m, priceArea.OrderPrice);

        var second = success.Seats.Single(s => s.SourceSeatId == "21353011007");
        Assert.Equal(2, second.SeatNumber);
        Assert.Equal("ss", second.RawStatus);
        Assert.Equal("21353011006", second.LeftNeighborSeatId);
        Assert.Null(second.RightNeighborSeatId);

        var third = success.Seats.Single(s => s.SourceSeatId == "21353011010");
        Assert.Equal("E", third.Row);
        Assert.Equal("sn", third.RawStatus); // unknown-to-us status codes are carried through verbatim
        Assert.Null(third.LeftNeighborSeatId);
        Assert.Null(third.RightNeighborSeatId);
    }

    [Fact]
    public async Task GetSeats_400_is_NotBookable()
    {
        var (client, _) = CreateSeatsClient(
            HttpStatusCode.BadRequest,
            """{"code":400,"message":"Diese Vorstellung ist aktuell nicht buchbar"}""");

        var result = await client.GetSeatsAsync("2135", "74666", CancellationToken.None);

        Assert.IsType<KinoheldSeatsResult.NotBookable>(result);
    }

    [Fact]
    public async Task GetSeats_404_is_NotFound()
    {
        var (client, _) = CreateSeatsClient(
            HttpStatusCode.NotFound,
            """{"code":404,"message":"Show not found."}""");

        var result = await client.GetSeatsAsync("2135", "74011", CancellationToken.None);

        Assert.IsType<KinoheldSeatsResult.NotFound>(result);
    }

    [Fact]
    public async Task GetSeats_403_is_Blocked()
    {
        var (client, _) = CreateSeatsClient(HttpStatusCode.Forbidden, "<html>Forbidden</html>");

        var result = await client.GetSeatsAsync("2135", "74705", CancellationToken.None);

        var blocked = Assert.IsType<KinoheldSeatsResult.Blocked>(result);
        Assert.Equal(403, blocked.StatusCode);
    }

    [Fact]
    public async Task GetSeats_429_is_Blocked()
    {
        var (client, _) = CreateSeatsClient(HttpStatusCode.TooManyRequests, "");

        var result = await client.GetSeatsAsync("2135", "74705", CancellationToken.None);

        var blocked = Assert.IsType<KinoheldSeatsResult.Blocked>(result);
        Assert.Equal(429, blocked.StatusCode);
    }

    [Fact]
    public async Task GetSeats_200_with_garbage_body_is_Anomalous()
    {
        var (client, _) = CreateSeatsClient(HttpStatusCode.OK, "<html>this is not the seats JSON</html>");

        var result = await client.GetSeatsAsync("2135", "74705", CancellationToken.None);

        Assert.IsType<KinoheldSeatsResult.Anomalous>(result);
    }

    [Fact]
    public async Task GetSeats_200_without_seats_object_is_Anomalous()
    {
        var (client, _) = CreateSeatsClient(HttpStatusCode.OK, """{"unexpected":true}""");

        var result = await client.GetSeatsAsync("2135", "74705", CancellationToken.None);

        Assert.IsType<KinoheldSeatsResult.Anomalous>(result);
    }

    [Fact]
    public async Task GetSeats_unexpected_status_is_Anomalous()
    {
        var (client, _) = CreateSeatsClient(HttpStatusCode.InternalServerError, "oops");

        var result = await client.GetSeatsAsync("2135", "74705", CancellationToken.None);

        var anomalous = Assert.IsType<KinoheldSeatsResult.Anomalous>(result);
        Assert.Contains("500", anomalous.Detail);
    }

    private sealed class StubHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody),
            });
    }

    private sealed class RecordingStubHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        public HttpMethod? LastRequestMethod { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestMethod = request.Method;
            LastRequestUri = request.RequestUri;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody),
            };
        }
    }
}
