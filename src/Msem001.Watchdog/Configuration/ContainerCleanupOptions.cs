namespace Msem001.Watchdog.Configuration;

/// <summary>
/// Removal of long running containers left over by builds (configuration section <c>Watchdog:Containers</c>).
/// </summary>
public sealed class ContainerCleanupOptions
{
    /// <summary>Whether long running containers are removed.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Interval of the container check.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Running containers started longer ago than this are removed.</summary>
    public TimeSpan MaxRunTime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Time a container gets to stop before it is killed (<c>podman rm --force --time</c>).</summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Containers whose name starts with one of these prefixes (case-insensitive) are never removed.
    /// Must be empty by default, the configuration binder appends to default list entries.
    /// </summary>
    public IList<string> ExcludedNamePrefixes { get; set; } = [];
}
