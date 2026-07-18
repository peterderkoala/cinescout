using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace cinescout.core.Kinoheld;

/// <summary>
/// Real HTTP implementation of <see cref="IKinoheldClient"/>. Fetches a booking link's widget
/// page (a plain GET — <see cref="HttpClient"/>'s default handler follows the 301 redirect from
/// a <c>bookingLink</c> URL to the cinema's canonical widget page automatically) and parses the
/// inline <c>dataLayer.push({...})</c> JSON blob embedded in the page for the auditorium list.
/// Never calls the ToS-restricted <c>/ajax/getSeats</c> endpoint.
/// </summary>
public sealed class KinoheldClient(HttpClient httpClient) : IKinoheldClient
{
    private const string DataLayerPushMarker = "dataLayer.push(";

    public async Task<KinoheldWidgetConfig> GetWidgetConfigAsync(string bookingLink, CancellationToken cancellationToken)
    {
        var html = await httpClient.GetStringAsync(bookingLink, cancellationToken);

        var dataLayer = ParseDataLayer(html);

        return new KinoheldWidgetConfig
        {
            Auditoriums = dataLayer.Cinema.Auditoriums
                .Select(a => new KinoheldAuditorium { Id = a.Id.ToString(), Name = a.Name })
                .ToList(),
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
