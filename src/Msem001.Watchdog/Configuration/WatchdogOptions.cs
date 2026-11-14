namespace Msem001.Watchdog.Configuration;

/// <summary>
/// Root options of the watchdog service (configuration section <c>Watchdog</c>).
/// </summary>
public sealed class WatchdogOptions
{
    /// <summary>Name of the configuration section.</summary>
    public const string SectionName = "Watchdog";

    /// <summary>Disk space monitoring and maintenance thresholds.</summary>
    public DiskOptions Disk { get; set; } = new();

    /// <summary>RAM usage monitoring.</summary>
    public MemoryOptions Memory { get; set; } = new();

    /// <summary>Scheduled checks (nightly pre-check).</summary>
    public ScheduleOptions Schedule { get; set; } = new();

    /// <summary>Podman machine and Docker API proxy monitoring.</summary>
    public PodmanOptions Podman { get; set; } = new();

    /// <summary>Removal of long running containers.</summary>
    public ContainerCleanupOptions Containers { get; set; } = new();

    /// <summary>PowerShell maintenance scripts.</summary>
    public ScriptOptions Scripts { get; set; } = new();

    /// <summary>Jenkins inbound agent process.</summary>
    public JenkinsAgentOptions JenkinsAgent { get; set; } = new();

    /// <summary>Mail alerting for errors and critical events.</summary>
    public MailOptions Mail { get; set; } = new();
}
