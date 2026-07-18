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

    [JsonPropertyName("performances")]
    public List<HallOfFamePerformanceGroupDto> PerformanceGroups { get; set; } = [];
}

/// <summary>
/// One attribute-group of showings for a film (e.g. plain showings, D-Box, etc). The dict is
/// keyed by performanceID (as a string).
/// </summary>
public class HallOfFamePerformanceGroupDto
{
    [JsonPropertyName("performances")]
    public Dictionary<string, HallOfFamePerformanceDto> Performances { get; set; } = [];
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
