namespace cinescout.core.Kinoheld;

/// <summary>
/// App-wide kill switch for all Kinoheld seat polling: the moment Kinoheld pushes back
/// (403/429 or any anomalous response), the breaker trips and every polling path — recurring
/// crawl, on-demand page fetch, force-refresh — stops calling out entirely. Deliberately
/// in-memory and permanent for v1: there is no automatic half-open/retry, and an app restart
/// is the "manual reset". Registered as a singleton.
/// </summary>
public sealed class KinoheldCircuitBreaker
{
    private sealed record TripRecord(string Reason, DateTimeOffset At);

    private TripRecord? _trip;

    public bool IsTripped => Volatile.Read(ref _trip) is not null;

    public string? TripReason => Volatile.Read(ref _trip)?.Reason;

    public DateTimeOffset? TrippedAt => Volatile.Read(ref _trip)?.At;

    /// <summary>Trips the breaker. The first trip wins — later calls never overwrite the original reason/timestamp.</summary>
    public void Trip(string reason) =>
        Interlocked.CompareExchange(ref _trip, new TripRecord(reason, DateTimeOffset.UtcNow), null);
}
