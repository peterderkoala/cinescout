using Hangfire;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace cinescout.web.HealthChecks;

/// <summary>
/// Coarse "is background job processing alive" signal for /healthz (#48/#52/#53): unhealthy once
/// every registered Hangfire server's heartbeat is older than 3x its own 30s default interval.
/// Deliberately not a functional "did the last crawl actually succeed" check — see #45's Out of
/// Scope.
/// </summary>
public sealed class HangfireHeartbeatHealthCheck(JobStorage jobStorage) : IHealthCheck
{
    private static readonly TimeSpan MaxHeartbeatAge = TimeSpan.FromSeconds(90);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var servers = jobStorage.GetMonitoringApi().Servers();

        if (servers.Count == 0)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy("No Hangfire server registered."));
        }

        var now = DateTime.UtcNow;
        var staleCount = servers.Count(s => s.Heartbeat is null || now - s.Heartbeat.Value > MaxHeartbeatAge);

        return Task.FromResult(staleCount == 0
            ? HealthCheckResult.Healthy($"{servers.Count} Hangfire server(s) reporting a recent heartbeat.")
            : HealthCheckResult.Unhealthy($"{staleCount} of {servers.Count} Hangfire server(s) haven't reported a heartbeat within {MaxHeartbeatAge}."));
    }
}
