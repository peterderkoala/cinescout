using System.Text.Json;
using System.Text.Json.Serialization;

namespace cinescout.core.HallOfFame;

/// <summary>
/// Top-level shape of the Hall-of-Fame "filtered-films" schedule response. Only the "films"
/// key is modeled — "filters", "scheduleView", "totalCount", "config" are ignored.
/// </summary>
public class HallOfFameScheduleResponse
{
    [JsonPropertyName("films")]
    public List<HallOfFameFilmDto> Films { get; set; } = [];
}

public class HallOfFameFilmDto
{
    [JsonPropertyName("detailId")]
    public int DetailId { get; set; }

    [JsonPropertyName("filmTitle")]
    public required string FilmTitle { get; set; }

    [JsonPropertyName("posterUrl")]
    public string? PosterUrl { get; set; }

    [JsonPropertyName("performances")]
    public List<HallOfFamePerformanceGroupDto> PerformanceGroups { get; set; } = [];
}

/// <summary>
/// One attribute-group of showings for a film (e.g. plain showings, D-Box, etc). The dict is
/// keyed by performanceID (as a string). Values are kept as raw <see cref="JsonElement"/>
/// (not deserialized directly into <see cref="HallOfFamePerformanceDto"/>) so callers can both
/// map the typed fields they need AND retain the exact raw JSON — via <c>GetRawText()</c> — for
/// PerformanceSnapshot's archival RawPayload, which must be the real upstream response, not a
/// re-serialization of only the fields this DTO happens to model.
/// </summary>
public class HallOfFamePerformanceGroupDto
{
    [JsonPropertyName("performances")]
    public Dictionary<string, JsonElement> Performances { get; set; } = [];
}

public class HallOfFamePerformanceDto
{
    [JsonPropertyName("performanceID")]
    public int PerformanceId { get; set; }

    [JsonPropertyName("bookingLink")]
    public required string BookingLink { get; set; }

    [JsonPropertyName("unixdatetime")]
    public long UnixDateTime { get; set; }

    [JsonPropertyName("isSoldOut")]
    public int IsSoldOut { get; set; }

    [JsonPropertyName("isNotBookable")]
    public int IsNotBookable { get; set; }

    [JsonPropertyName("isOnline")]
    public int IsOnline { get; set; }

    [JsonPropertyName("saleIsAllowed")]
    public int SaleIsAllowed { get; set; }
}
