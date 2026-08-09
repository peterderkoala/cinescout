namespace cinescout.model;

/// <summary>
/// A named physical auditorium at a Cinema, sourced from Kinoheld's widget config — the
/// Hall-of-Fame schedule API has no room concept at all.
/// </summary>
public class Room
{
    public int Id { get; set; }
    public int CinemaId { get; set; }
    public required string ExternalAuditoriumId { get; set; }
    public required string Name { get; set; }
}
