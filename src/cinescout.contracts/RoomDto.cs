namespace cinescout.contracts;

/// <summary>
/// A genuinely 1:1 wire shape for <c>cinescout.model.Room</c> — the worked Mapperly example the
/// convention's later tickets are expected to copy. See <c>cinescout.web.Mapping.RoomMapper</c> for
/// the generated mapper and why it lives there rather than here: cinescout.contracts is a true leaf
/// and can never reference cinescout.model, so a Mapperly-generated mapper that needs both
/// <c>Room</c> and <c>RoomDto</c> visible in the same compilation can't be defined in this project.
/// </summary>
public sealed record RoomDto
{
    public required int Id { get; init; }
    public required int CinemaId { get; init; }
    public required string ExternalAuditoriumId { get; init; }
    public required string Name { get; init; }
}
