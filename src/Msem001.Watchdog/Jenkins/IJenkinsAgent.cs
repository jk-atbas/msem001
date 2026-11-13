namespace Msem001.Watchdog.Jenkins;

/// <summary>
/// Controls the Jenkins inbound agent process.
/// </summary>
internal interface IJenkinsAgent
{
    /// <summary>Whether an agent process for the configured node is running (no matter who started it).</summary>
    /// <returns><see langword="true"/> if at least one agent process is running.</returns>
    bool IsRunning();

    /// <summary>Terminates all agent processes of the configured node.</summary>
    /// <returns>A task that completes when the processes are terminated.</returns>
    Task StopAsync(CancellationToken cancellationToken);

    /// <summary>Starts the agent and connects it to the controller, unless it is already running.</summary>
    /// <returns>A task that completes when the start was verified.</returns>
    Task StartAsync(CancellationToken cancellationToken);
}
