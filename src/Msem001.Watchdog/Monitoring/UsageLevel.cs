namespace Msem001.Watchdog.Monitoring;

/// <summary>
/// Classification of a resource usage (disk, RAM).
/// </summary>
internal enum UsageLevel
{
    Normal = 0,
    High = 1,
    Critical = 2,
}
