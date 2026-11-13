namespace Msem001.Watchdog.Podman;

/// <summary>
/// Determines the <see cref="PodmanHealth"/>.
/// </summary>
internal interface IPodmanHealthChecker
{
    Task<PodmanHealth> CheckAsync(CancellationToken cancellationToken);
}
