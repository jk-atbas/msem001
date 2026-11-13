namespace Msem001.Watchdog.Maintenance;

/// <summary>
/// What triggered a disk maintenance check.
/// </summary>
internal enum MaintenanceTrigger
{
    /// <summary>Cyclic disk check, subject to the maintenance cooldown.</summary>
    Periodic = 0,

    /// <summary>Daily pre-check before the nightly Jenkins job, ignores the cooldown.</summary>
    Nightly = 1,
}
