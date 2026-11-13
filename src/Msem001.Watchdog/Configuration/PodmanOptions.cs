namespace Msem001.Watchdog.Configuration;

/// <summary>
/// Podman machine and Docker API proxy (win-sshproxy) monitoring.
/// </summary>
public sealed class PodmanOptions
{
    /// <summary>Podman CLI executable.</summary>
    public string Executable { get; set; } = "podman";

    /// <summary>Name of the Podman machine.</summary>
    public string MachineName { get; set; } = "podman-machine-default";

    /// <summary>Named pipe of the Docker API forwarding (provided by win-sshproxy).</summary>
    public string DockerApiPipeName { get; set; } = "docker_engine";

    /// <summary>Delay after service start before the first health check (system boot settles).</summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Interval of the cyclic health check.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Number of consecutive failed proxy checks of a running machine before it is reset.</summary>
    public int FailureThreshold { get; set; } = 2;

    /// <summary>After an unsuccessful repair the next attempt is made after this interval at the earliest.</summary>
    public TimeSpan RepairRetryInterval { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Timeout of the Docker API ping.</summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Timeout of short Podman CLI commands (inspect).</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Timeout of <c>podman machine start</c>.</summary>
    public TimeSpan StartTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Waiting time after a machine start until the proxy is checked.</summary>
    public TimeSpan ProxySettleTime { get; set; } = TimeSpan.FromSeconds(20);
}
