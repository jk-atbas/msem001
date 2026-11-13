namespace Msem001.Watchdog.Configuration;

/// <summary>
/// Scheduled checks.
/// </summary>
public sealed class ScheduleOptions
{
    /// <summary>
    /// Local time of the daily disk pre-check before the nightly Jenkins job (22:00 minus one hour minus buffer).
    /// </summary>
    public TimeOnly NightlyPreCheckTime { get; set; } = new(20, 45);
}
