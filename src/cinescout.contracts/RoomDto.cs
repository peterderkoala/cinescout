namespace cinescout.contracts;

/// <summary>
/// A genuinely 1:1 wire shape for <c>cinescout.model.Room</c> — the worked Mapperly example the
/// convention's later tickets are expected to copy. See <c>cinescout.web.Mapping.RoomMapper</c> for
/// the generated mapper.
/// </summary>
public sealed record RoomDto
{
    public required int Id { get; init; }
    public required int CinemaId { get; init; }
    public required string ExternalAuditoriumId { get; init; }
    public required string Name { get; init; }
}
