namespace Msem001.Watchdog.Podman;

/// <summary>
/// Lists and removes containers of the Podman machine.
/// </summary>
internal interface IPodmanContainers
{
    /// <summary>Lists all containers, running and not running (without pod infra containers).</summary>
    /// <returns>The containers, or <see langword="null"/> if the list could not be read (logged).</returns>
    Task<IReadOnlyList<PodmanContainer>?> ListAsync(CancellationToken cancellationToken);

    /// <summary>Stops and removes a container including its anonymous volumes.</summary>
    /// <returns><see langword="true"/> if the container is gone afterwards.</returns>
    Task<bool> RemoveAsync(PodmanContainer container, CancellationToken cancellationToken);
}
