using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Maintenance;

namespace Msem001.Watchdog.Workers;

/// <summary>
/// Runs the <see cref="BuildAgentHealthMonitor"/> cyclically (Podman machine, win-sshproxy, Jenkins agent).
/// </summary>
internal sealed partial class BuildAgentHealthWorker(
    BuildAgentHealthMonitor monitor,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<BuildAgentHealthWorker> logger) : BackgroundService
{
    internal async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await monitor.CheckAsync(cancellationToken).ConfigureAwait(false);
            LogCycleFinished(logger, outcome);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCycleFailed(logger, ex);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Podman;
        LogStarted(logger, settings.StartupDelay, settings.CheckInterval);

        try
        {
            await Task.Delay(settings.StartupDelay, timeProvider, stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunCycleAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(options.Value.Podman.CheckInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(EventId = 12000, Level = LogLevel.Information, Message = "Überwachung von Podman, win-sshproxy und Jenkins-Agent startet in {StartupDelay} (Intervall {Interval})")]
    private static partial void LogStarted(ILogger logger, TimeSpan startupDelay, TimeSpan interval);

    [LoggerMessage(EventId = 12001, Level = LogLevel.Debug, Message = "Health-Check abgeschlossen: {Outcome}")]
    private static partial void LogCycleFinished(ILogger logger, HealthCheckOutcome outcome);

    [LoggerMessage(EventId = 12002, Level = LogLevel.Error, Message = "Health-Check fehlgeschlagen")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception);
}
