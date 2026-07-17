namespace cinescout.model;

/// <summary>
/// A Film the user has marked as interesting. Whether it's still "active" (has upcoming
/// performances) is a query-time filter, not a stored flag.
/// </summary>
public class WatchedMovie
{
    public int Id { get; set; }
    public int FilmId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
