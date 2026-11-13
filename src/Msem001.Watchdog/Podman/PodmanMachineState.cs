namespace Msem001.Watchdog.Podman;

/// <summary>
/// State of the Podman machine as reported by <c>podman machine inspect</c>.
/// </summary>
internal enum PodmanMachineState
{
    Unknown = 0,
    Missing = 1,
    Stopped = 2,
    Starting = 3,
    Running = 4,
}
