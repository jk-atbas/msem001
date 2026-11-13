namespace Msem001.Watchdog.Configuration;

/// <summary>
/// Disk space monitoring and maintenance thresholds.
/// </summary>
public sealed class DiskOptions
{
    /// <summary>Drive that is monitored.</summary>
    public string DriveName { get; set; } = @"C:\";

    /// <summary>Below this amount of free space (GB) the Jenkins workspace cleanup runs.</summary>
    public double CleanupThresholdGb { get; set; } = 50;

    /// <summary>If the free space (GB) after the cleanup is still below this value, the Podman machine is reset.</summary>
    public double PodmanResetThresholdGb { get; set; } = 200;

    /// <summary>Disk usage (percent) above which an error is reported.</summary>
    public double ErrorUsagePercent { get; set; } = 90;

    /// <summary>Disk usage (percent) above which a critical event is reported.</summary>
    public double CriticalUsagePercent { get; set; } = 95;

    /// <summary>Interval of the periodic disk check.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Minimum time between two periodic maintenance runs (prevents killing the agent in a loop).</summary>
    public TimeSpan MaintenanceCooldown { get; set; } = TimeSpan.FromHours(2);

    /// <summary>A still existing high disk usage is reported again after this interval.</summary>
    public TimeSpan UsageAlertRepeatInterval { get; set; } = TimeSpan.FromHours(6);
}
