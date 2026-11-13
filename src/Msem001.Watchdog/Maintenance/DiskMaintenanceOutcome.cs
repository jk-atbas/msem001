namespace Msem001.Watchdog.Maintenance;

/// <summary>
/// Result of a disk maintenance check.
/// </summary>
internal enum DiskMaintenanceOutcome
{
    /// <summary>Enough free space, nothing done.</summary>
    NotRequired = 0,

    /// <summary>Too little space, but a maintenance ran recently.</summary>
    SkippedCooldown = 1,

    /// <summary>The Jenkins workspace was cleaned.</summary>
    CleanupOnly = 2,

    /// <summary>The Jenkins workspace was cleaned and the Podman machine was reset.</summary>
    CleanupAndPodmanReset = 3,

    /// <summary>Too little space, but a maintenance routine is already running (e.g. waiting for builds).</summary>
    SkippedBusy = 4,
}
