using Msem001.Watchdog.Processes;

namespace Msem001.Watchdog.Podman;

/// <summary>
/// Combines machine state, Docker API ping and the number of win-sshproxy processes.
/// </summary>
internal sealed partial class PodmanHealthChecker(
    IPodmanMachine machine,
    IDockerApiProbe dockerApiProbe,
    ISystemProcesses processes,
    ILogger<PodmanHealthChecker> logger) : IPodmanHealthChecker
{
    internal const string SshProxyProcessName = "win-sshproxy";

    public async Task<PodmanHealth> CheckAsync(CancellationToken cancellationToken)
    {
        var state = await machine.GetStateAsync(cancellationToken).ConfigureAwait(false);
        var proxyCount = processes.CountByName(SshProxyProcessName);

        PodmanHealth health;
        if (state == PodmanMachineState.Running)
        {
            var probe = await dockerApiProbe.ProbeAsync(cancellationToken).ConfigureAwait(false);
            health = new PodmanHealth(state, probe.IsReachable, proxyCount, probe.Detail);
        }
        else
        {
            health = new PodmanHealth(state, false, proxyCount, "Docker-API nicht geprüft, Machine läuft nicht");
        }

        LogHealth(logger, health.IsHealthy, health.MachineState, health.DockerApiReachable, health.SshProxyProcessCount, health.Detail);
        return health;
    }

    [LoggerMessage(EventId = 4100, Level = LogLevel.Debug, Message = "Podman-Health: gesund={IsHealthy}, Machine={MachineState}, Docker-API={DockerApiReachable}, win-sshproxy-Prozesse={SshProxyProcessCount}, {Detail}")]
    private static partial void LogHealth(ILogger logger, bool isHealthy, PodmanMachineState machineState, bool dockerApiReachable, int sshProxyProcessCount, string detail);
}
