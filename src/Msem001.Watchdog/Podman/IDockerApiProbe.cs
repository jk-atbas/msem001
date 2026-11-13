namespace Msem001.Watchdog.Podman;

/// <summary>
/// Checks whether the Docker API forwarding (win-sshproxy) answers.
/// </summary>
internal interface IDockerApiProbe
{
    Task<DockerApiProbeResult> ProbeAsync(CancellationToken cancellationToken);
}
