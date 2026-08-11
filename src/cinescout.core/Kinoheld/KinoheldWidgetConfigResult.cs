namespace cinescout.core.Kinoheld;

/// <summary>
/// Classified outcome of a widget-config GET, mirroring <see cref="KinoheldSeatsResult"/>'s
/// block-vs-anomaly split — there's no per-cinema "expected" failure equivalent to getSeats'
/// 400/404 here, only success or a service push-back that must trip
/// <see cref="KinoheldCircuitBreaker"/>.
/// </summary>
public abstract record KinoheldWidgetConfigResult
{
    private KinoheldWidgetConfigResult()
    {
    }

    public sealed record Success(KinoheldWidgetConfig Config) : KinoheldWidgetConfigResult;

    /// <summary>HTTP 403 or 429 — Kinoheld pushed back. Trips the circuit breaker.</summary>
    public sealed record Blocked(int StatusCode) : KinoheldWidgetConfigResult;

    /// <summary>Any other non-2xx status, or a body that didn't parse as expected. Trips the circuit breaker.</summary>
    public sealed record Anomalous(string Detail) : KinoheldWidgetConfigResult;
}
