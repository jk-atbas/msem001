using System.Diagnostics;
using Msem001.Watchdog.Jenkins;

namespace Msem001.Watchdog.Maintenance;

/// <summary>
/// Serializes all maintenance routines and wraps them with stopping and restarting the Jenkins agent.
/// Before stopping, running builds may finish (<see cref="IJenkinsAgentDrain"/>).
/// </summary>
internal sealed partial class MaintenanceExecutor(IJenkinsAgent agent, IJenkinsAgentDrain drain, ILogger<MaintenanceExecutor> logger) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>Whether a maintenance routine is currently running.</summary>
    public bool IsBusy => gate.CurrentCount == 0;

    /// <summary>
    /// Waits for running builds, stops the agent, runs the routine and always reconnects the agent afterwards.
    /// </summary>
    /// <returns>The result of the routine.</returns>
    public Task<T> RunWithAgentStoppedAsync<T>(string routineName, Func<CancellationToken, Task<T>> routine, CancellationToken cancellationToken) =>
        RunWithAgentStoppedAsync(routineName, routine, isUrgent: null, cancellationToken);

    /// <summary>
    /// Waits for running builds (unless <paramref name="isUrgent"/> returns <see langword="true"/>), stops the agent,
    /// runs the routine and always reconnects the agent afterwards.
    /// </summary>
    /// <returns>The result of the routine.</returns>
    public async Task<T> RunWithAgentStoppedAsync<T>(string routineName, Func<CancellationToken, Task<T>> routine, Func<bool>? isUrgent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(routine);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            LogRoutineStarting(logger, routineName);
            var markedOffline = await drain.DrainAsync(routineName, isUrgent, cancellationToken).ConfigureAwait(false);
            try
            {
                await StopAgentAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    return await routine(cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    // Reconnect even if the routine failed or the service is shutting down.
                    await StartAgentAsync().ConfigureAwait(false);
                    LogRoutineFinished(logger, routineName, stopwatch.Elapsed);
                }
            }
            finally
            {
                await drain.RestoreAsync(markedOffline, CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Starts the agent if it is not running. Does nothing while a maintenance routine runs.
    /// </summary>
    /// <returns><see langword="false"/> if skipped because of a running maintenance routine.</returns>
    public async Task<bool> TryEnsureAgentRunningAsync(CancellationToken cancellationToken)
    {
        if (!await gate.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
            return false;

        try
        {
            if (!agent.IsRunning())
            {
                LogAgentNotRunning(logger);
                await agent.StartAsync(cancellationToken).ConfigureAwait(false);
            }

            await drain.ReleaseStaleOfflineAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    [LoggerMessage(EventId = 6000, Level = LogLevel.Information, Message = "Wartungsroutine '{RoutineName}' beginnt")]
    private static partial void LogRoutineStarting(ILogger logger, string routineName);

    [LoggerMessage(EventId = 6001, Level = LogLevel.Information, Message = "Wartungsroutine '{RoutineName}' abgeschlossen nach {Duration}")]
    private static partial void LogRoutineFinished(ILogger logger, string routineName, TimeSpan duration);

    [LoggerMessage(EventId = 6002, Level = LogLevel.Error, Message = "Jenkins-Agent konnte vor der Wartung nicht beendet werden, Wartung wird trotzdem ausgeführt")]
    private static partial void LogAgentStopFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6003, Level = LogLevel.Error, Message = "Jenkins-Agent konnte nach der Wartung nicht wieder gestartet werden")]
    private static partial void LogAgentStartFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 6004, Level = LogLevel.Warning, Message = "Jenkins-Agent läuft nicht und wird gestartet")]
    private static partial void LogAgentNotRunning(ILogger logger);

    private async Task StopAgentAsync(CancellationToken cancellationToken)
    {
        try
        {
            await agent.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogAgentStopFailed(logger, ex);
        }
    }

    private async Task StartAgentAsync()
    {
        try
        {
            await agent.StartAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogAgentStartFailed(logger, ex);
        }
    }
}
