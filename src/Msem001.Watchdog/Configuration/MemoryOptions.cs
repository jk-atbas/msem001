namespace Msem001.Watchdog.Configuration;

/// <summary>
/// RAM usage monitoring (configuration section <c>Watchdog:Memory</c>).
/// </summary>
public sealed class MemoryOptions
{
    /// <summary>Whether the RAM usage is monitored.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>RAM usage (percent) above which an error is reported.</summary>
    public double ErrorUsagePercent { get; set; } = 90;

    /// <summary>RAM usage (percent) above which a critical event is reported.</summary>
    public double CriticalUsagePercent { get; set; } = 95;

    /// <summary>Interval of the RAM check.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// A threshold must be exceeded continuously for this time before it is reported.
    /// Builds cause short peaks that are normal; <see cref="TimeSpan.Zero"/> reports immediately.
    /// </summary>
    public TimeSpan SustainedDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>A still existing high RAM usage is reported again after this interval.</summary>
    public TimeSpan UsageAlertRepeatInterval { get; set; } = TimeSpan.FromHours(6);
}
