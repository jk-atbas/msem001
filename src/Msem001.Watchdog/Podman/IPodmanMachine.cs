namespace Msem001.Watchdog.Podman;

/// <summary>
/// Controls the Podman machine via the Podman CLI.
/// </summary>
internal interface IPodmanMachine
{
    /// <summary>Reads the state of the machine.</summary>
    /// <returns>The machine state.</returns>
    Task<PodmanMachineState> GetStateAsync(CancellationToken cancellationToken);

    /// <summary>Starts the machine.</summary>
    /// <returns><see langword="true"/> if it is running afterwards (or already was).</returns>
    Task<bool> StartAsync(CancellationToken cancellationToken);
}
