namespace Msem001.Watchdog.Podman;

/// <summary>
/// A running container as reported by <c>podman ps</c>.
/// </summary>
/// <param name="Id">Full container ID.</param>
/// <param name="Name">First container name.</param>
/// <param name="Image">Image reference.</param>
/// <param name="StartedAt">Start time of the container.</param>
internal sealed record PodmanContainer(string Id, string Name, string Image, DateTimeOffset StartedAt);
