namespace Msem001.Watchdog.Podman;

/// <summary>
/// Health of the Podman machine including the Docker API forwarding.
/// </summary>
internal sealed record PodmanHealth(PodmanMachineState MachineState, bool DockerApiReachable, int SshProxyProcessCount, string Detail)
{
    public bool IsHealthy => MachineState == PodmanMachineState.Running && DockerApiReachable;

    public override string ToString() =>
        $"Machine={MachineState}, DockerApi={(DockerApiReachable ? "OK" : "nicht erreichbar")}, win-sshproxy-Prozesse={SshProxyProcessCount}, {Detail}";
}
