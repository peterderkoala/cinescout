namespace cinescout.model;

/// <summary>
/// Kinoheld seat status codes. "sf" (free) and "ss" (sold) are confirmed from live
/// responses; "sn" and "src" were seen only in client-side JS and never in a live
/// response, so they map to Other pending confirmation of their exact meaning.
/// </summary>
public enum SeatOccupancyStatus
{
    Free,
    Sold,
    Other,
}
