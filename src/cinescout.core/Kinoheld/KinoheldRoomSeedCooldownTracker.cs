namespace cinescout.core.Kinoheld;

/// <summary>
/// Per-cinema cooldown for Re-seed Rooms requests: at most one outgoing widget-config call per
/// cinema every <see cref="Cooldown"/> — a sibling to <see cref="KinoheldFetchCooldownTracker"/>,
/// but a longer window (ADR 0004/0005: room seeding is a heavier operation than a single-performance
/// seat fetch). In-memory singleton, thread-safe via a plain lock.
/// </summary>
public sealed class KinoheldRoomSeedCooldownTracker
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);

    private readonly Lock _lock = new();
    private readonly Dictionary<int, DateTimeOffset> _lastSeedAt = [];

    /// <summary>
    /// Returns false (without updating anything) when the last recorded seed attempt for the
    /// cinema was less than <see cref="Cooldown"/> before <paramref name="now"/>; otherwise records
    /// <paramref name="now"/> as the attempt time and returns true.
    /// </summary>
    public bool TryBeginSeed(int cinemaId, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (_lastSeedAt.TryGetValue(cinemaId, out var last) && now - last < Cooldown)
            {
                return false;
            }

            _lastSeedAt[cinemaId] = now;
            return true;
        }
    }
}
