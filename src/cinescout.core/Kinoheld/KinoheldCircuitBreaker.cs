namespace cinescout.core.Kinoheld;

/// <summary>
/// App-wide kill switch for all Kinoheld seat polling: the moment Kinoheld pushes back
/// (403/429 or any anomalous response), the breaker trips and every polling path — recurring
/// crawl, on-demand page fetch, force-refresh — stops calling out entirely. Deliberately
/// in-memory and permanent for v1: there is no automatic half-open/retry, and an app restart
/// is the "manual reset". Registered as a singleton; thread-safe via a plain lock.
/// </summary>
public sealed class KinoheldCircuitBreaker
{
    private readonly Lock _lock = new();
    private bool _isTripped;
    private string? _tripReason;
    private DateTimeOffset? _trippedAt;

    public bool IsTripped
    {
        get
        {
            lock (_lock)
            {
                return _isTripped;
            }
        }
    }

    public string? TripReason
    {
        get
        {
            lock (_lock)
            {
                return _tripReason;
            }
        }
    }

    public DateTimeOffset? TrippedAt
    {
        get
        {
            lock (_lock)
            {
                return _trippedAt;
            }
        }
    }

    /// <summary>Trips the breaker. The first trip wins — later calls never overwrite the original reason/timestamp.</summary>
    public void Trip(string reason)
    {
        lock (_lock)
        {
            if (_isTripped)
            {
                return;
            }

            _isTripped = true;
            _tripReason = reason;
            _trippedAt = DateTimeOffset.UtcNow;
        }
    }
}
