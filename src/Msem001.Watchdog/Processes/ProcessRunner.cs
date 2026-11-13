using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Msem001.Watchdog.Processes.Abstractions;

namespace Msem001.Watchdog.Processes;

/// <summary>
/// <see cref="IProcessRunner"/> based on <see cref="IProcessFactory"/>.
/// </summary>
/// <remarks>
/// Only the exit of the process itself is awaited, not the end of its output streams:
/// <c>podman machine start</c> spawns long-running helpers (win-sshproxy, gvproxy) that may inherit the
/// redirected handles and would otherwise block forever.
/// </remarks>
internal sealed partial class ProcessRunner(IProcessFactory processFactory, TimeProvider timeProvider, ILogger<ProcessRunner> logger) : IProcessRunner
{
    internal static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(30);

    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = CreateStartInfo(request);
        var standardOutput = new ConcurrentQueue<string>();
        var standardError = new ConcurrentQueue<string>();
        var outputClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorLevel = request.OutputLogLevel > LogLevel.Warning ? request.OutputLogLevel : LogLevel.Warning;

        var argumentText = string.Join(' ', request.Arguments);
        LogStarting(logger, request.DisplayName, request.FileName, argumentText);
        var startedAt = timeProvider.GetTimestamp();

        IProcess process;
        try
        {
            process = processFactory.Start(startInfo);
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException($"Prozess '{request.FileName}' konnte nicht gestartet werden: {ex.Message}", ex);
        }

        using (process)
        {
            process.OutputLineReceived += (_, e) =>
            {
                if (e.Line is null)
                {
                    outputClosed.TrySetResult();
                    return;
                }

                standardOutput.Enqueue(e.Line);
                LogOutputLine(logger, request.OutputLogLevel, request.DisplayName, e.Line);
            };
            process.ErrorLineReceived += (_, e) =>
            {
                if (e.Line is null)
                {
                    errorClosed.TrySetResult();
                    return;
                }

                standardError.Enqueue(e.Line);
                LogErrorLine(logger, errorLevel, request.DisplayName, e.Line);
            };
            process.Exited += (_, _) => exited.TrySetResult();
            if (process.HasExited)
                exited.TrySetResult();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var timedOut = false;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(request.Timeout);
                try
                {
                    await exited.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await KillTreeAsync(process, request.DisplayName).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    timedOut = true;
                    LogTimedOut(logger, request.DisplayName, request.Timeout);
                }
            }

            // Give the output a short time to drain, but do not depend on EOF (see remarks).
            await Task.WhenAny(
                    Task.WhenAll(outputClosed.Task, errorClosed.Task),
                    Task.Delay(OutputDrainTimeout, timeProvider, CancellationToken.None))
                .ConfigureAwait(false);

            var duration = timeProvider.GetElapsedTime(startedAt);
            var exitCode = timedOut ? -1 : process.ExitCode;
            LogFinished(logger, request.DisplayName, exitCode, duration);

            return new ProcessRunResult(exitCode, timedOut, [.. standardOutput], [.. standardError], duration);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(ProcessRunRequest request)
    {
        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
        };

        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);

        return startInfo;
    }

    [LoggerMessage(EventId = 2000, Level = LogLevel.Debug, Message = "Starte {ProcessName}: {FileName} {Arguments}")]
    private static partial void LogStarting(ILogger logger, string processName, string fileName, string arguments);

    [LoggerMessage(EventId = 2001, Message = "[{ProcessName}] {Line}")]
    private static partial void LogOutputLine(ILogger logger, LogLevel level, string processName, string line);

    [LoggerMessage(EventId = 2002, Message = "[{ProcessName}] (stderr) {Line}")]
    private static partial void LogErrorLine(ILogger logger, LogLevel level, string processName, string line);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug, Message = "{ProcessName} beendet mit Exit-Code {ExitCode} nach {Duration}")]
    private static partial void LogFinished(ILogger logger, string processName, int exitCode, TimeSpan duration);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Warning, Message = "{ProcessName} hat das Timeout von {Timeout} überschritten und wurde beendet")]
    private static partial void LogTimedOut(ILogger logger, string processName, TimeSpan timeout);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Warning, Message = "{ProcessName} konnte nicht sauber beendet werden")]
    private static partial void LogKillFailed(ILogger logger, Exception exception, string processName);

    private async Task KillTreeAsync(IProcess process, string displayName)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            using var killTimeout = new CancellationTokenSource(KillTimeout);
            await process.WaitForExitAsync(killTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or OperationCanceledException)
        {
            LogKillFailed(logger, ex, displayName);
        }
    }
}
