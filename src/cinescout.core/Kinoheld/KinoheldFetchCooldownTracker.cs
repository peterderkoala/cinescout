namespace cinescout.core.Kinoheld;

/// <summary>
/// Per-performance cooldown for on-demand seat fetches (including force-refresh): at most one
/// outgoing getSeats call per performance every <see cref="Cooldown"/>. In-memory singleton,
/// thread-safe via a plain lock.
/// </summary>
public sealed class KinoheldFetchCooldownTracker
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    private readonly Lock _lock = new();
    private readonly Dictionary<int, DateTimeOffset> _lastFetchAt = [];

    /// <summary>
    /// Returns false (without updating anything) when the last recorded fetch for the performance
    /// was less than <see cref="Cooldown"/> before <paramref name="now"/>; otherwise records
    /// <paramref name="now"/> as the fetch time and returns true.
    /// </summary>
    public bool TryBeginFetch(int performanceId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_lastFetchAt.TryGetValue(performanceId, out var last) && now - last < Cooldown)
            {
                return false;
            }

            _lastFetchAt[performanceId] = now;
            return true;
        }
    }
}
