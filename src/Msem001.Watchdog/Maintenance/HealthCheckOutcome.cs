namespace Msem001.Watchdog.Maintenance;

/// <summary>
/// Result of a cyclic build agent health check.
/// </summary>
internal enum HealthCheckOutcome
{
    Healthy = 0,

    /// <summary>Unhealthy, but the failure threshold is not reached yet.</summary>
    Degraded = 1,

    /// <summary>Unhealthy and repaired successfully.</summary>
    Repaired = 2,

    /// <summary>Unhealthy and the repair failed.</summary>
    RepairFailed = 3,

    /// <summary>Unhealthy, but the last repair failed only recently.</summary>
    RepairDeferred = 4,

    /// <summary>A maintenance routine is running.</summary>
    SkippedBusy = 5,
}
