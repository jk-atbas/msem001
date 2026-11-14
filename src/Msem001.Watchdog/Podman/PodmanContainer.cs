namespace Msem001.Watchdog.Podman;

/// <summary>
/// A container as reported by <c>podman ps --all</c>.
/// </summary>
/// <param name="Id">Full container ID.</param>
/// <param name="Name">First container name.</param>
/// <param name="Image">Image reference.</param>
/// <param name="State">Podman state, e.g. <c>running</c>, <c>created</c>, <c>exited</c>.</param>
/// <param name="CreatedAt">Creation time.</param>
/// <param name="StartedAt">Last start, <see langword="null"/> if never started.</param>
/// <param name="ExitedAt">Last exit, <see langword="null"/> if never exited.</param>
internal sealed record PodmanContainer(
    string Id,
    string Name,
    string Image,
    string State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? ExitedAt)
{
    /// <summary>Whether the container is running (also paused or stopping).</summary>
    public bool IsRunning => State.ToUpperInvariant() is "RUNNING" or "PAUSED" or "STOPPING";

    /// <summary>Whether the container is being removed right now.</summary>
    public bool IsRemoving => State.Equals("removing", StringComparison.OrdinalIgnoreCase);

    /// <summary>Since when the container is in its current state: started (running) or exited/created (not running).</summary>
    public DateTimeOffset StateSince => IsRunning ? StartedAt ?? CreatedAt : ExitedAt ?? CreatedAt;
}
