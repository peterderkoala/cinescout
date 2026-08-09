namespace cinescout.core.Kinoheld;

/// <summary>
/// Abstracts talking to Kinoheld: fetching a cinema's widget config (auditorium list, cinema id) —
/// the server-rendered HTML page reached by following a <c>bookingLink</c>'s redirect — and fetching
/// per-performance seat availability via <c>POST /ajax/getSeats</c>. Seat fetches are deliberately
/// honest and self-limiting: a non-spoofed User-Agent, no header spoofing, and every call is gated
/// by the app-level <see cref="KinoheldCircuitBreaker"/>, which permanently stops all Kinoheld
/// polling the moment the service pushes back (403/429/anything anomalous).
/// </summary>
public interface IKinoheldClient
{
    Task<KinoheldWidgetConfig> GetWidgetConfigAsync(string bookingLink, CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the seat map + availability for one performance. Never throws on a non-success
    /// HTTP status — the status is classified into a <see cref="KinoheldSeatsResult"/> variant so
    /// the caller can distinguish expected per-performance outcomes (400 not-bookable, 404 gone)
    /// from block/anomaly signals that must trip the circuit breaker.
    /// </summary>
    /// <param name="cinemaId">Kinoheld's numeric cinema id (<see cref="cinescout.model.Cinema.KinoheldCinemaId"/>), sent as <c>cid</c>.</param>
    /// <param name="showId">Kinoheld's show id (<see cref="cinescout.model.Performance.SourcePerformanceId"/>), sent as <c>showId</c>.</param>
    Task<KinoheldSeatsResult> GetSeatsAsync(string cinemaId, string showId, CancellationToken cancellationToken);
}

/// <summary>
/// The subset of a Kinoheld widget page's inline config this app cares about — the cinema's
/// auditorium list (used to seed <see cref="cinescout.model.Room"/> rows) and its numeric
/// cinema id (needed as <c>cid</c> by the seat-availability fetch).
/// </summary>
public sealed class KinoheldWidgetConfig
{
    /// <summary>
    /// Kinoheld's numeric cinema id (dataLayer <c>cinema.id</c>, e.g. 2135), stringified. NOT the
    /// opaque <c>cinema.cid</c> string, which is a different, GraphQL-facing identifier.
    /// </summary>
    public required string CinemaId { get; init; }

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
