namespace cinescout.contracts;

/// <summary>Request body for <c>POST /api/cinemas</c> and <c>PUT /api/cinemas/{id}</c> — the full editable field set (§5.1's detail form).</summary>
public sealed record CinemaWriteRequest
{
    public required string Name { get; init; }
    public required string ExternalCinemaId { get; init; }
    public required string CrawlBaseUrl { get; init; }
    public required bool IsActive { get; init; }
}

/// <summary>Request body for <c>PUT /api/cinemas/{cinemaId}/rooms/{roomId}</c> — Room's one user-editable field (§5.1: ExternalAuditoriumId is provider-owned).</summary>
public sealed record RoomRenameRequest
{
    public required string Name { get; init; }
}
