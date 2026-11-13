using Msem001.Watchdog.Processes;

namespace Msem001.Watchdog.Scripts;

/// <summary>
/// The PowerShell maintenance scripts of the repository.
/// </summary>
internal interface IMaintenanceScripts
{
    /// <summary>Runs <c>scripts\Clean-JenkinsWorkspace.ps1</c>.</summary>
    /// <returns>The script result.</returns>
    Task<ProcessRunResult> RunJenkinsWorkspaceCleanupAsync(CancellationToken cancellationToken);

    /// <summary>Runs <c>scripts\Reset-PodmanMachine.ps1 -Force</c>.</summary>
    /// <returns>The script result.</returns>
    Task<ProcessRunResult> RunPodmanResetAsync(CancellationToken cancellationToken);
}
