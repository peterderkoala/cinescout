namespace cinescout.core.Kinoheld;

/// <summary>
/// Classified outcome of a <c>POST /ajax/getSeats</c> call. The variants encode the crawl
/// posture's central distinction: <see cref="NotBookable"/> (HTTP 400) and <see cref="NotFound"/>
/// (HTTP 404) are expected per-performance outcomes to log and skip, while <see cref="Blocked"/>
/// (403/429) and <see cref="Anomalous"/> (anything else unexpected) mean the service pushed back
/// and must trip the <see cref="KinoheldCircuitBreaker"/>.
/// </summary>
public abstract record KinoheldSeatsResult
{
    private KinoheldSeatsResult()
    {
    }

    /// <summary>HTTP 200 with a parseable seat map.</summary>
    /// <param name="RawPayload">The verbatim response body (valid JSON), archived per snapshot.</param>
    /// <param name="Seats">The parsed per-seat entries.</param>
    public sealed record Success(string RawPayload, IReadOnlyList<KinoheldSeat> Seats) : KinoheldSeatsResult;

    /// <summary>HTTP 400 — "Diese Vorstellung ist aktuell nicht buchbar". Expected; retried next cycle.</summary>
    public sealed record NotBookable : KinoheldSeatsResult;

    /// <summary>HTTP 404 — "Show not found." (expired/removed). Expected.</summary>
    public sealed record NotFound : KinoheldSeatsResult;

    /// <summary>HTTP 403 or 429 — Kinoheld pushed back. Trips the circuit breaker.</summary>
    public sealed record Blocked(int StatusCode) : KinoheldSeatsResult;

    /// <summary>Any other non-200 status, or a 200 whose body isn't the expected shape. Trips the circuit breaker.</summary>
    public sealed record Anomalous(string Detail) : KinoheldSeatsResult;
}

/// <summary>
/// One seat from a getSeats response, narrowed to what CineScout persists — Kinoheld's purely
/// cosmetic/rendering fields (pixel position, size, icons, price areas) are ignored.
/// </summary>
/// <param name="SourceSeatId">Kinoheld's own seat id (the key in the response's "seats" object), e.g. "21353011006".</param>
/// <param name="Row">Row letter ("r"), e.g. "D".</param>
/// <param name="SeatNumber">Seat number within the row ("n" — a JSON string like "1" upstream).</param>
/// <param name="RawStatus">Kinoheld's status code verbatim: "sf" free, "ss" sold, anything else unknown.</param>
/// <param name="LeftNeighborSeatId">Seat id to the left ("sl"); null when upstream sends the number 0 (no neighbor).</param>
/// <param name="RightNeighborSeatId">Seat id to the right ("sr"); null when upstream sends the number 0 (no neighbor).</param>
/// <param name="SectorId">Auditorium external id ("secId", a string) — resolves against <see cref="cinescout.model.Room.ExternalAuditoriumId"/>.</param>
public sealed record KinoheldSeat(
    string SourceSeatId,
    string Row,
    int SeatNumber,
    string RawStatus,
    string? LeftNeighborSeatId,
    string? RightNeighborSeatId,
    string SectorId);
