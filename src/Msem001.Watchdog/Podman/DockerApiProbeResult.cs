namespace Msem001.Watchdog.Podman;

/// <summary>
/// Result of a Docker API ping via the win-sshproxy named pipe.
/// </summary>
internal sealed record DockerApiProbeResult(bool IsReachable, string Detail);
