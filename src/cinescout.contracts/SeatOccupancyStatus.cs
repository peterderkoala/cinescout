namespace cinescout.contracts;

/// <summary>
/// A self-contained mirror of <c>cinescout.model.SeatOccupancyStatus</c>. cinescout.contracts is a
/// true leaf and must never reference cinescout.model, so it cannot use that type directly — same
/// rationale as <see cref="Weekday"/> mirroring <c>DaysOfWeekFlags</c>. Values line up 1:1 with the
/// model enum (Free/Sold/Other); keep them in lockstep if the model ever changes.
/// </summary>
public enum SeatOccupancyStatus
{
    Free,
    Sold,
    Other,
}
