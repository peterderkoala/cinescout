namespace cinescout.contracts;

/// <summary>
/// Read model for a <c>FavoriteTimeWindow</c> row (§5.3). <see cref="DayCodes"/> is the §6.3
/// pre-computed Mo..Su chip list — never the raw <c>DaysOfWeekFlags</c>/<see cref="Weekday"/> value
/// — matching <see cref="DayCodeConverter"/>'s convention that read DTOs carry the chip shape, not
/// the bit-flag one.
/// </summary>
public sealed record TimeWindowDto
{
    public required int Id { get; init; }
    public required string[] DayCodes { get; init; }
    public required TimeOnly StartTime { get; init; }
    public required TimeOnly EndTime { get; init; }
}
