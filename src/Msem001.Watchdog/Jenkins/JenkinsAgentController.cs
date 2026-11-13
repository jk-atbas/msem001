using System.Diagnostics;
using System.IO.Abstractions;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Processes;

namespace Msem001.Watchdog.Jenkins;

/// <summary>
/// Starts and stops <c>java -jar agent.jar -url ... -secret ... -name ... -webSocket -workDir ...</c>.
/// </summary>
/// <remarks>
/// The agent runs as child process of the service and therefore with the service account
/// (local administrator, full token because service logons are not UAC filtered).
/// Running agents are detected by their command line, so agents started manually are found as well.
/// </remarks>
internal sealed partial class JenkinsAgentController(
    ISystemProcesses processes,
    IFileSystem fileSystem,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILoggerFactory loggerFactory,
    ILogger<JenkinsAgentController> logger) : IJenkinsAgent, IDisposable
{
    internal const string OutputLoggerCategory = "Msem001.Watchdog.Jenkins.AgentOutput";
    private static readonly string[] JavaImageNames = ["java.exe", "javaw.exe"];

    private readonly ILogger outputLogger = loggerFactory.CreateLogger(OutputLoggerCategory);
    private readonly Lock sync = new();
    private IRunningProcess? ownedProcess;

    public bool IsRunning() => FindAgentProcesses().Count > 0;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.JenkinsAgent;
        if (!settings.Enabled)
            return;

        var agents = FindAgentProcesses();
        if (agents.Count == 0)
        {
            LogNotRunning(logger, settings.AgentName);
        }

        foreach (var agent in agents)
        {
            LogStopping(logger, settings.AgentName, agent.ProcessId);
            var stopped = await processes.KillTreeAsync(agent.ProcessId, settings.StopTimeout, cancellationToken).ConfigureAwait(false);
            if (stopped)
                LogStopped(logger, settings.AgentName, agent.ProcessId);
            else
                LogStopFailed(logger, settings.AgentName, agent.ProcessId, settings.StopTimeout);
        }

        ReplaceOwnedProcess(null);
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.JenkinsAgent;
        if (!settings.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        var running = FindAgentProcesses();
        if (running.Count > 0)
        {
            LogAlreadyRunning(logger, settings.AgentName, running[0].ProcessId);
            return;
        }

        if (!fileSystem.File.Exists(settings.AgentJarPath))
        {
            LogAgentJarMissing(logger, settings.AgentJarPath);
            return;
        }

        var startInfo = CreateStartInfo(settings, fileSystem.Path);
        var maskedArguments = FormatArguments(startInfo.ArgumentList, settings.Secret);
        LogStarting(logger, settings.AgentName, settings.ControllerUrl, settings.JavaExecutable, maskedArguments);

        IRunningProcess process;
        try
        {
            process = processes.Start(startInfo, line => LogAgentOutput(outputLogger, line));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogStartFailed(logger, ex, settings.AgentName);
            return;
        }

        ReplaceOwnedProcess(process);

        await Task.Delay(settings.StartupGracePeriod, timeProvider, cancellationToken).ConfigureAwait(false);
        if (process.HasExited)
            LogExitedImmediately(logger, settings.AgentName, process.ExitCode, settings.StartupGracePeriod);
        else
            LogStarted(logger, settings.AgentName, process.Id);
    }

    public void Dispose() => ReplaceOwnedProcess(null);

    internal static bool IsAgentCommandLine(string? commandLine, JenkinsAgentOptions settings, IPath path)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return false;

        var jarName = path.GetFileName(settings.AgentJarPath);
        return commandLine.Contains(jarName, StringComparison.OrdinalIgnoreCase)
            && commandLine.Contains(settings.AgentName, StringComparison.OrdinalIgnoreCase);
    }

    internal static ProcessStartInfo CreateStartInfo(JenkinsAgentOptions settings, IPath path)
    {
        var startInfo = new ProcessStartInfo(settings.JavaExecutable)
        {
            WorkingDirectory = path.GetDirectoryName(path.GetFullPath(settings.AgentJarPath)) ?? settings.WorkDir,
        };

        foreach (var argument in BuildArguments(settings))
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    internal static IReadOnlyList<string> BuildArguments(JenkinsAgentOptions settings)
    {
        var arguments = new List<string>
        {
            "-jar",
            settings.AgentJarPath,
            "-url",
            settings.ControllerUrl,
            "-secret",
            settings.Secret,
            "-name",
            settings.AgentName,
        };

        if (settings.UseWebSocket)
            arguments.Add("-webSocket");

        arguments.Add("-workDir");
        arguments.Add(settings.WorkDir);
        return arguments;
    }

    internal static string FormatArguments(IEnumerable<string> arguments, string secret) =>
        string.Join(' ', arguments.Select(a => !string.IsNullOrEmpty(secret) && a == secret ? "***" : a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a));

    [LoggerMessage(EventId = 5000, Level = LogLevel.Information, Message = "Jenkins-Agent {AgentName} läuft nicht, nichts zu beenden")]
    private static partial void LogNotRunning(ILogger logger, string agentName);

    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Beende Jenkins-Agent {AgentName} (PID {ProcessId})")]
    private static partial void LogStopping(ILogger logger, string agentName, int processId);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Jenkins-Agent {AgentName} (PID {ProcessId}) beendet")]
    private static partial void LogStopped(ILogger logger, string agentName, int processId);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Error, Message = "Jenkins-Agent {AgentName} (PID {ProcessId}) konnte nicht innerhalb von {Timeout} beendet werden")]
    private static partial void LogStopFailed(ILogger logger, string agentName, int processId, TimeSpan timeout);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Debug, Message = "Verwaltung des Jenkins-Agents ist deaktiviert")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 5005, Level = LogLevel.Information, Message = "Jenkins-Agent {AgentName} läuft bereits (PID {ProcessId})")]
    private static partial void LogAlreadyRunning(ILogger logger, string agentName, int processId);

    [LoggerMessage(EventId = 5006, Level = LogLevel.Error, Message = "Jenkins-Agent kann nicht gestartet werden, agent.jar fehlt: {AgentJarPath}")]
    private static partial void LogAgentJarMissing(ILogger logger, string agentJarPath);

    [LoggerMessage(EventId = 5007, Level = LogLevel.Information, Message = "Starte Jenkins-Agent {AgentName} gegen {ControllerUrl}: {JavaExecutable} {Arguments}")]
    private static partial void LogStarting(ILogger logger, string agentName, string controllerUrl, string javaExecutable, string arguments);

    [LoggerMessage(EventId = 5008, Level = LogLevel.Error, Message = "Jenkins-Agent {AgentName} konnte nicht gestartet werden")]
    private static partial void LogStartFailed(ILogger logger, Exception exception, string agentName);

    [LoggerMessage(EventId = 5009, Level = LogLevel.Error, Message = "Jenkins-Agent {AgentName} hat sich innerhalb von {GracePeriod} wieder beendet (Exit-Code {ExitCode})")]
    private static partial void LogExitedImmediately(ILogger logger, string agentName, int? exitCode, TimeSpan gracePeriod);

    [LoggerMessage(EventId = 5010, Level = LogLevel.Information, Message = "Jenkins-Agent {AgentName} läuft (PID {ProcessId})")]
    private static partial void LogStarted(ILogger logger, string agentName, int processId);

    [LoggerMessage(EventId = 5011, Level = LogLevel.Information, Message = "{Line}")]
    private static partial void LogAgentOutput(ILogger logger, string line);

    private List<ProcessSnapshot> FindAgentProcesses()
    {
        var settings = options.Value.JenkinsAgent;
        return [.. JavaImageNames.SelectMany(processes.FindByImageName).Where(p => IsAgentCommandLine(p.CommandLine, settings, fileSystem.Path))];
    }

    private void ReplaceOwnedProcess(IRunningProcess? process)
    {
        IRunningProcess? previous;
        lock (sync)
        {
            previous = ownedProcess;
            ownedProcess = process;
        }

        if (!ReferenceEquals(previous, process))
            previous?.Dispose();
    }
}
