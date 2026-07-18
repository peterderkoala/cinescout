namespace cinescout.core.Kinoheld;

/// <summary>
/// Abstracts fetching a Kinoheld cinema's widget config (auditorium list, etc.) — the
/// server-rendered HTML page reached by following a <c>bookingLink</c>'s redirect. This does
/// NOT poll the ToS-restricted <c>/ajax/getSeats</c> endpoint at all; it only ever does a plain
/// GET of the widget page.
/// </summary>
public interface IKinoheldClient
{
    Task<KinoheldWidgetConfig> GetWidgetConfigAsync(string bookingLink, CancellationToken cancellationToken);
}

/// <summary>
/// The subset of a Kinoheld widget page's inline config this app cares about — currently just
/// the cinema's auditorium list, used to seed <see cref="cinescout.model.Room"/> rows.
/// </summary>
public sealed class KinoheldWidgetConfig
{
    public required IReadOnlyList<KinoheldAuditorium> Auditoriums { get; init; }
}

public sealed class KinoheldAuditorium
{
    /// <summary>
    /// Kinoheld's numeric auditorium id, stringified to match <see cref="cinescout.model.Room.ExternalAuditoriumId"/>'s type.
    /// </summary>
    public required string Id { get; init; }

    public required string Name { get; init; }
}
