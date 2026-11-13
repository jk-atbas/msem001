using System.IO.Abstractions;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Processes;

namespace Msem001.Watchdog.Scripts;

/// <summary>
/// Runs the maintenance scripts with PowerShell. The script output is logged line by line.
/// </summary>
internal sealed partial class PowerShellMaintenanceScripts(
    IProcessRunner processRunner,
    IFileSystem fileSystem,
    IHostEnvironment hostEnvironment,
    IOptions<WatchdogOptions> options,
    ILogger<PowerShellMaintenanceScripts> logger) : IMaintenanceScripts
{
    private const string ForceSwitch = "-Force";

    public Task<ProcessRunResult> RunJenkinsWorkspaceCleanupAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Scripts;
        return RunScriptAsync(settings.CleanupScript, settings.CleanupArguments, settings.CleanupTimeout, cancellationToken);
    }

    public Task<ProcessRunResult> RunPodmanResetAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Scripts;
        return RunScriptAsync(settings.PodmanResetScript, BuildPodmanResetArguments(settings.PodmanResetArguments), settings.PodmanResetTimeout, cancellationToken);
    }

    internal static string ResolveScriptPath(IPath path, string scriptsDirectory, string scriptFile, string baseDirectory)
    {
        if (path.IsPathRooted(scriptFile))
            return path.GetFullPath(scriptFile);

        var directory = string.IsNullOrWhiteSpace(scriptsDirectory) ? baseDirectory : path.Combine(baseDirectory, scriptsDirectory);
        return path.GetFullPath(path.Combine(directory, scriptFile));
    }

    internal static IReadOnlyList<string> BuildPodmanResetArguments(IEnumerable<string> additionalArguments) =>
        [ForceSwitch, .. additionalArguments.Where(a => !string.Equals(a, ForceSwitch, StringComparison.OrdinalIgnoreCase))];

    internal static IReadOnlyList<string> BuildArguments(string scriptPath, IEnumerable<string> scriptArguments) =>
    [
        "-NoProfile",
        "-NonInteractive",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        scriptPath,
        .. scriptArguments,
    ];

    [LoggerMessage(EventId = 3000, Level = LogLevel.Information, Message = "Starte Wartungsskript {ScriptName} ({ScriptPath})")]
    private static partial void LogScriptStarting(ILogger logger, string scriptName, string scriptPath);

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Wartungsskript {ScriptName} erfolgreich nach {Duration}")]
    private static partial void LogScriptSucceeded(ILogger logger, string scriptName, TimeSpan duration);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Error, Message = "Wartungsskript {ScriptName} fehlgeschlagen (Exit-Code {ExitCode}, Dauer {Duration}). Fehlerausgabe: {StandardError}")]
    private static partial void LogScriptFailed(ILogger logger, string scriptName, int exitCode, TimeSpan duration, string standardError);

    [LoggerMessage(EventId = 3003, Level = LogLevel.Error, Message = "Wartungsskript {ScriptName} nach {Timeout} abgebrochen (Timeout)")]
    private static partial void LogScriptTimedOut(ILogger logger, string scriptName, TimeSpan timeout);

    private async Task<ProcessRunResult> RunScriptAsync(
        string scriptFile,
        IEnumerable<string> scriptArguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var settings = options.Value.Scripts;
        var scriptPath = ResolveScriptPath(fileSystem.Path, settings.ScriptsDirectory, scriptFile, hostEnvironment.ContentRootPath);
        if (!fileSystem.File.Exists(scriptPath))
            throw new FileNotFoundException($"Wartungsskript nicht gefunden: {scriptPath}", scriptPath);

        var scriptName = fileSystem.Path.GetFileName(scriptPath);
        var arguments = BuildArguments(scriptPath, scriptArguments);
        LogScriptStarting(logger, scriptName, scriptPath);

        var request = new ProcessRunRequest(settings.PowerShellExecutable, arguments)
        {
            DisplayName = scriptName,
            Timeout = timeout,
            WorkingDirectory = fileSystem.Path.GetDirectoryName(scriptPath),
            OutputLogLevel = LogLevel.Information,
        };

        var result = await processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
            LogScriptSucceeded(logger, scriptName, result.Duration);
        else if (result.TimedOut)
            LogScriptTimedOut(logger, scriptName, timeout);
        else
            LogScriptFailed(logger, scriptName, result.ExitCode, result.Duration, result.StandardErrorText);

        return result;
    }
}
