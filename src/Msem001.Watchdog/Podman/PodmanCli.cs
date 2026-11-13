using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Processes;

namespace Msem001.Watchdog.Podman;

/// <summary>
/// <see cref="IPodmanMachine"/> based on the Podman CLI.
/// </summary>
internal sealed partial class PodmanCli(
    IProcessRunner processRunner,
    IOptions<WatchdogOptions> options,
    ILogger<PodmanCli> logger) : IPodmanMachine
{
    public async Task<PodmanMachineState> GetStateAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Podman;
        var request = new ProcessRunRequest(settings.Executable, ["machine", "inspect", "--format", "{{.State}}", settings.MachineName])
        {
            DisplayName = "podman machine inspect",
            Timeout = settings.CommandTimeout,
            OutputLogLevel = LogLevel.Trace,
        };

        var result = await processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
            return ParseState(result.StandardOutputText);

        var state = IsMissingMachineError(result.StandardErrorText) ? PodmanMachineState.Missing : PodmanMachineState.Unknown;
        LogInspectFailed(logger, settings.MachineName, result.ExitCode, state, result.StandardErrorText);
        return state;
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Podman;
        LogStarting(logger, settings.MachineName);

        var request = new ProcessRunRequest(settings.Executable, ["machine", "start", settings.MachineName])
        {
            DisplayName = "podman machine start",
            Timeout = settings.StartTimeout,
            OutputLogLevel = LogLevel.Information,
        };

        var result = await processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded || IsAlreadyRunningError(result.StandardErrorText))
        {
            LogStarted(logger, settings.MachineName);
            return true;
        }

        LogStartFailed(logger, settings.MachineName, result.ExitCode, result.TimedOut, result.StandardErrorText);
        return false;
    }

    internal static PodmanMachineState ParseState(string output) => output.Trim().ToUpperInvariant() switch
    {
        "RUNNING" => PodmanMachineState.Running,
        "STOPPED" => PodmanMachineState.Stopped,
        "STARTING" => PodmanMachineState.Starting,
        _ => PodmanMachineState.Unknown,
    };

    internal static bool IsMissingMachineError(string standardError) =>
        standardError.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
        || standardError.Contains("no such", StringComparison.OrdinalIgnoreCase);

    internal static bool IsAlreadyRunningError(string standardError) =>
        standardError.Contains("already running", StringComparison.OrdinalIgnoreCase);

    [LoggerMessage(EventId = 4000, Level = LogLevel.Warning, Message = "podman machine inspect {MachineName} fehlgeschlagen (Exit-Code {ExitCode}, Zustand {State}): {StandardError}")]
    private static partial void LogInspectFailed(ILogger logger, string machineName, int exitCode, PodmanMachineState state, string standardError);

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "Starte Podman-Machine {MachineName}")]
    private static partial void LogStarting(ILogger logger, string machineName);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information, Message = "Podman-Machine {MachineName} gestartet")]
    private static partial void LogStarted(ILogger logger, string machineName);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "Podman-Machine {MachineName} konnte nicht gestartet werden (Exit-Code {ExitCode}, Timeout {TimedOut}): {StandardError}")]
    private static partial void LogStartFailed(ILogger logger, string machineName, int exitCode, bool timedOut, string standardError);
}
