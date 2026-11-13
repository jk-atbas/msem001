namespace Msem001.Watchdog.Jenkins;

/// <summary>
/// Lets running builds finish before a maintenance stops the agent ("graceful maintenance").
/// </summary>
internal interface IJenkinsAgentDrain
{
    /// <summary>
    /// Marks the node temporarily offline (no new builds) and waits until it is idle, at most <paramref name="timeout"/>.
    /// Never throws except on cancellation.
    /// </summary>
    /// <param name="routineName">Name of the maintenance routine, shown as offline reason in Jenkins.</param>
    /// <param name="isUrgent">Optional check; once it returns <see langword="true"/> the wait ends immediately.</param>
    /// <param name="timeout">Maximum wait; <see langword="null"/> = <see cref="Configuration.JenkinsAgentOptions.DrainTimeout"/>.</param>
    /// <param name="cancellationToken">Cancellation token. On cancellation the node is brought back online.</param>
    /// <returns><see langword="true"/> if the node was marked offline by the watchdog and must be restored with <see cref="RestoreAsync"/>.</returns>
    Task<bool> DrainAsync(string routineName, Func<bool>? isUrgent, TimeSpan? timeout, CancellationToken cancellationToken);

    /// <summary>Brings the node back online if <paramref name="markedOffline"/> is set. Never throws.</summary>
    /// <returns>A task that completes when done.</returns>
    Task RestoreAsync(bool markedOffline, CancellationToken cancellationToken);

    /// <summary>
    /// Brings the node back online if it is still marked offline by the watchdog (e.g. after a crash or a failed restore).
    /// Calls the API only after start and after a failed restore. Never throws except on cancellation.
    /// </summary>
    /// <returns>A task that completes when done.</returns>
    Task ReleaseStaleOfflineAsync(CancellationToken cancellationToken);
}
