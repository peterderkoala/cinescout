using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace cinescout.core.Kinoheld;

/// <summary>
/// Real HTTP implementation of <see cref="IKinoheldClient"/>. Widget config comes from a plain GET
/// of a booking link's widget page (<see cref="HttpClient"/>'s default handler follows the 301
/// redirect from a <c>bookingLink</c> URL to the cinema's canonical widget page automatically),
/// parsing the inline <c>dataLayer.push({...})</c> JSON blob for the cinema id + auditorium list.
/// Seat availability comes from <c>POST /ajax/getSeats</c> — called only through the app-level
/// circuit-breaker/cooldown gating in <see cref="KinoheldSeatCrawlService"/>, with an honest
/// non-spoofed User-Agent configured on the underlying <see cref="HttpClient"/>.
/// </summary>
public sealed class KinoheldClient(HttpClient httpClient) : IKinoheldClient
{
    private const string DataLayerPushMarker = "dataLayer.push(";
    private const string GetSeatsUrl = "https://www.kinoheld.de/ajax/getSeats";

    public async Task<KinoheldWidgetConfig> GetWidgetConfigAsync(string bookingLink, CancellationToken cancellationToken)
    {
        var html = await httpClient.GetStringAsync(bookingLink, cancellationToken);

        var dataLayer = ParseDataLayer(html);

        return new KinoheldWidgetConfig
        {
            CinemaId = dataLayer.Cinema.Id.ToString(CultureInfo.InvariantCulture),
            Auditoriums = dataLayer.Cinema.Auditoriums
                .Select(a => new KinoheldAuditorium { Id = a.Id.ToString(), Name = a.Name })
                .ToList(),
        };
    }

    public async Task<KinoheldSeatsResult> GetSeatsAsync(string cinemaId, string showId, CancellationToken cancellationToken)
    {
        // The exact request shape the widget's own JS builds: form-urlencoded POST, no cookies,
        // no session, no spoofed headers.
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["cid"] = cinemaId,
            ["showId"] = showId,
            ["mode"] = "widget",
        });

        // Do NOT throw on non-success: 400/404 are expected per-performance outcomes and
        // 403/429/etc must be surfaced as data for the circuit breaker, not exceptions.
        using var response = await httpClient.PostAsync(GetSeatsUrl, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return (int)response.StatusCode switch
        {
            200 => ParseSeats(body),
            400 => new KinoheldSeatsResult.NotBookable(),
            404 => new KinoheldSeatsResult.NotFound(),
            403 or 429 => new KinoheldSeatsResult.Blocked((int)response.StatusCode),
            var status => new KinoheldSeatsResult.Anomalous($"Unexpected HTTP {status} from getSeats."),
        };
    }

    /// <summary>
    /// Parses a 200 getSeats body. Top-level <c>seats</c> is a JSON OBJECT keyed by seat id (not
    /// an array); <c>n</c> is a string like "1"; <c>sl</c>/<c>sr</c> are either a string seat id
    /// or the NUMBER 0 meaning "no neighbor" — both JSON types must be handled. Anything that
    /// doesn't match this shape is classified as <see cref="KinoheldSeatsResult.Anomalous"/>.
    /// </summary>
    private static KinoheldSeatsResult ParseSeats(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("seats", out var seatsElement)
                || seatsElement.ValueKind != JsonValueKind.Object)
            {
                return new KinoheldSeatsResult.Anomalous("200 response body has no top-level \"seats\" object.");
            }

            var seats = new List<KinoheldSeat>();
            foreach (var seatProperty in seatsElement.EnumerateObject())
            {
                var seat = seatProperty.Value;
                seats.Add(new KinoheldSeat(
                    SourceSeatId: seatProperty.Name,
                    Row: seat.GetProperty("r").GetString() ?? throw new JsonException("Seat \"r\" was null."),
                    SeatNumber: ParseSeatNumber(seat.GetProperty("n")),
                    RawStatus: seat.GetProperty("status").GetString() ?? throw new JsonException("Seat \"status\" was null."),
                    LeftNeighborSeatId: ParseNeighbor(seat, "sl"),
                    RightNeighborSeatId: ParseNeighbor(seat, "sr"),
                    SectorId: seat.GetProperty("secId").GetString() ?? throw new JsonException("Seat \"secId\" was null."),
                    PriceAreaProviderId: seat.TryGetProperty("p", out var p) ? p.GetString() : null));
            }

            var priceAreas = ParsePriceAreas(document.RootElement);

            return new KinoheldSeatsResult.Success(body, seats, priceAreas);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return new KinoheldSeatsResult.Anomalous($"200 response body did not parse as the expected seats shape: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses the response's top-level "priceAreas" array (absent or empty is valid — not every
    /// response necessarily carries pricing, and a missing array shouldn't itself be Anomalous).
    /// </summary>
    private static IReadOnlyList<KinoheldPriceArea> ParsePriceAreas(JsonElement root)
    {
        if (!root.TryGetProperty("priceAreas", out var priceAreasElement) || priceAreasElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var priceAreas = new List<KinoheldPriceArea>();
        foreach (var area in priceAreasElement.EnumerateArray())
        {
            priceAreas.Add(new KinoheldPriceArea(
                Id: area.GetProperty("id").GetString() ?? throw new JsonException("Price area \"id\" was null."),
                ProviderId: area.GetProperty("providerId").GetString() ?? throw new JsonException("Price area \"providerId\" was null."),
                Name: area.GetProperty("name").GetString() ?? throw new JsonException("Price area \"name\" was null."),
                OrderPrice: decimal.Parse(
                    area.GetProperty("orderPrice").GetString() ?? throw new JsonException("Price area \"orderPrice\" was null."),
                    CultureInfo.InvariantCulture)));
        }

        return priceAreas;
    }

    private static int ParseSeatNumber(JsonElement n) => n.ValueKind switch
    {
        JsonValueKind.String => int.Parse(n.GetString()!, CultureInfo.InvariantCulture),
        JsonValueKind.Number => n.GetInt32(),
        _ => throw new JsonException($"Seat \"n\" had unexpected kind {n.ValueKind}."),
    };

    private static string? ParseNeighbor(JsonElement seat, string propertyName)
    {
        if (!seat.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            // A string id — a real neighbor.
            JsonValueKind.String => value.GetString(),
            // The number 0 means "no neighbor"; treat any numeric value as an id otherwise.
            JsonValueKind.Number when value.GetInt64() == 0 => null,
            JsonValueKind.Number => value.GetInt64().ToString(CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    /// <summary>
    /// Extracts and parses the JSON object literal passed to <c>dataLayer.push(...)</c> in the
    /// widget page. Uses <see cref="Utf8JsonReader"/> as a proper JSON tokenizer to find the
    /// matching end of the object — a naive brace-counting text scan would miscount because the
    /// blob contains raw markup (e.g. nav icon SVGs) with braces/parens inside string values
    /// elsewhere in the object.
    /// </summary>
    private static KinoheldDataLayerDto ParseDataLayer(string html)
    {
        var pushIndex = html.IndexOf(DataLayerPushMarker, StringComparison.Ordinal);
        if (pushIndex < 0)
        {
            throw new InvalidOperationException("Kinoheld widget page did not contain the expected dataLayer.push(...) block.");
        }

        var objStart = html.IndexOf('{', pushIndex);
        if (objStart < 0)
        {
            throw new InvalidOperationException("Kinoheld widget page's dataLayer.push(...) block did not contain a JSON object.");
        }

        var remaining = Encoding.UTF8.GetBytes(html.Substring(objStart));
        var reader = new Utf8JsonReader(remaining);
        reader.Read(); // StartObject
        var depth = 1;
        while (depth > 0 && reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                depth++;
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                depth--;
            }
        }

        var objJson = html.Substring(objStart, checked((int)reader.BytesConsumed));

        return JsonSerializer.Deserialize<KinoheldDataLayerDto>(objJson)
            ?? throw new InvalidOperationException("Kinoheld widget page's dataLayer.push(...) block deserialized to null.");
    }

    private sealed class KinoheldDataLayerDto
    {
        [JsonPropertyName("cinema")]
        public required KinoheldCinemaDto Cinema { get; init; }
    }

    private sealed class KinoheldCinemaDto
    {
        /// <summary>The numeric cinema id used as "cid" by /ajax/* calls — NOT the opaque "cid" string field.</summary>
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("auditoriums")]
        public required List<KinoheldAuditoriumDto> Auditoriums { get; init; }
    }

    private sealed class KinoheldAuditoriumDto
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }
    }
}
