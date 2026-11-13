using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Podman;

namespace Msem001.Watchdog.Maintenance;

/// <summary>
/// Cyclic check that the Podman machine runs and the win-sshproxy Docker API forwarding answers,
/// repairs it if necessary and keeps the Jenkins agent connected.
/// </summary>
internal sealed partial class BuildAgentHealthMonitor(
    IPodmanHealthChecker healthChecker,
    PodmanRecovery podmanRecovery,
    MaintenanceExecutor executor,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<BuildAgentHealthMonitor> logger)
{
    private int consecutiveFailures;
    private DateTimeOffset? lastFailedRepairAt;

    public async Task<HealthCheckOutcome> CheckAsync(CancellationToken cancellationToken)
    {
        if (executor.IsBusy)
        {
            LogSkippedBusy(logger);
            return HealthCheckOutcome.SkippedBusy;
        }

        var settings = options.Value.Podman;
        var health = await healthChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (health.IsHealthy)
        {
            if (consecutiveFailures > 0 || lastFailedRepairAt is not null)
                LogRecovered(logger, health);

            consecutiveFailures = 0;
            lastFailedRepairAt = null;
            await executor.TryEnsureAgentRunningAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckOutcome.Healthy;
        }

        consecutiveFailures++;
        LogUnhealthy(logger, consecutiveFailures, settings.FailureThreshold, health);

        // A stopped machine (typical after a reboot) is handled immediately, a flaky proxy only after the threshold.
        var machineDown = health.MachineState != PodmanMachineState.Running;
        if (!machineDown && consecutiveFailures < settings.FailureThreshold)
            return HealthCheckOutcome.Degraded;

        var now = timeProvider.GetUtcNow();
        if (lastFailedRepairAt is { } lastFailure && now - lastFailure < settings.RepairRetryInterval)
        {
            LogRepairDeferred(logger, lastFailure, settings.RepairRetryInterval);
            await executor.TryEnsureAgentRunningAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckOutcome.RepairDeferred;
        }

        var repaired = await executor
            .RunWithAgentStoppedAsync("Podman-Reparatur", token => podmanRecovery.EnsureHealthyAsync(allowReset: true, token), cancellationToken)
            .ConfigureAwait(false);

        if (repaired.IsHealthy)
        {
            consecutiveFailures = 0;
            lastFailedRepairAt = null;
            return HealthCheckOutcome.Repaired;
        }

        lastFailedRepairAt = timeProvider.GetUtcNow();
        return HealthCheckOutcome.RepairFailed;
    }

    [LoggerMessage(EventId = 8000, Level = LogLevel.Debug, Message = "Health-Check übersprungen, eine Wartungsroutine läuft")]
    private static partial void LogSkippedBusy(ILogger logger);

    [LoggerMessage(EventId = 8001, Level = LogLevel.Information, Message = "Podman und Docker-API-Weiterleitung wieder gesund: {Health}")]
    private static partial void LogRecovered(ILogger logger, PodmanHealth health);

    [LoggerMessage(EventId = 8002, Level = LogLevel.Warning, Message = "Podman bzw. Docker-API-Weiterleitung nicht gesund ({Failures}/{Threshold}): {Health}")]
    private static partial void LogUnhealthy(ILogger logger, int failures, int threshold, PodmanHealth health);

    [LoggerMessage(EventId = 8003, Level = LogLevel.Warning, Message = "Letzte Podman-Reparatur um {LastFailureAt} fehlgeschlagen, nächster Versuch frühestens nach {RetryInterval}")]
    private static partial void LogRepairDeferred(ILogger logger, DateTimeOffset lastFailureAt, TimeSpan retryInterval);
}
