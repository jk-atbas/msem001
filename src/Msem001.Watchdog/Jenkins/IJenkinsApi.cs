namespace Msem001.Watchdog.Jenkins;

/// <summary>
/// Jenkins controller REST API for the configured agent node.
/// </summary>
internal interface IJenkinsApi
{
    /// <summary>Gets a value indicating whether user and API token are configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Reads the current state of the node.</summary>
    /// <returns>The node state.</returns>
    Task<JenkinsNodeStatus> GetNodeStatusAsync(CancellationToken cancellationToken);

    /// <summary>Toggles the "temporarily offline" flag of the node (<c>POST /computer/&lt;name&gt;/toggleOffline</c>).</summary>
    /// <param name="offlineMessage">Reason shown in Jenkins when marking offline; <see langword="null"/> when bringing it back online.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the controller accepted the request.</returns>
    Task ToggleOfflineAsync(string? offlineMessage, CancellationToken cancellationToken);
}
