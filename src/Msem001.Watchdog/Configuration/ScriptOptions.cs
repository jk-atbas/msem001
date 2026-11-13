namespace Msem001.Watchdog.Configuration;

/// <summary>
/// PowerShell maintenance scripts.
/// </summary>
public sealed class ScriptOptions
{
    /// <summary>PowerShell executable (<c>powershell.exe</c> or <c>pwsh.exe</c>).</summary>
    public string PowerShellExecutable { get; set; } = "pwsh.exe";

    /// <summary>Directory of the scripts. Relative paths are resolved against the service directory.</summary>
    public string ScriptsDirectory { get; set; } = "scripts";

    /// <summary>Script that cleans the Jenkins workspace.</summary>
    public string CleanupScript { get; set; } = "Clean-JenkinsWorkspace.ps1";

    /// <summary>Arguments of the cleanup script.</summary>
    public IList<string> CleanupArguments { get; set; } = [];

    /// <summary>Timeout of the cleanup script.</summary>
    public TimeSpan CleanupTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Script that resets the Podman machine.</summary>
    public string PodmanResetScript { get; set; } = "Reset-PodmanMachine.ps1";

    /// <summary>
    /// Additional arguments of the Podman reset script. <c>-Force</c> is always passed because the service runs non-interactive.
    /// </summary>
    /// <remarks>
    /// Collections must default to empty: the configuration binder appends configured items to existing ones.
    /// </remarks>
    public IList<string> PodmanResetArguments { get; set; } = [];

    /// <summary>Timeout of the Podman reset script.</summary>
    public TimeSpan PodmanResetTimeout { get; set; } = TimeSpan.FromMinutes(30);
}
