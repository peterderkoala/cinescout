namespace cinescout.contracts;

/// <summary>Request body for <c>POST /api/time-preferences</c> and <c>PUT /api/time-preferences/{id}</c> (§5.3's editor card).</summary>
public sealed record TimeWindowWriteRequest
{
    public required string[] DayCodes { get; init; }
    public required TimeOnly StartTime { get; init; }
    public required TimeOnly EndTime { get; init; }
}
