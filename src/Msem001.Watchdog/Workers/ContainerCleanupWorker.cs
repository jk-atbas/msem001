using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Maintenance;

namespace Msem001.Watchdog.Workers;

/// <summary>
/// Runs the <see cref="ContainerCleanup"/> cyclically.
/// </summary>
internal sealed partial class ContainerCleanupWorker(
    ContainerCleanup cleanup,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<ContainerCleanupWorker> logger) : BackgroundService
{
    internal async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            await cleanup.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCycleFailed(logger, ex);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Containers;
        if (!settings.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        var startupDelay = options.Value.Podman.StartupDelay;
        LogStarted(logger, settings.MaxRunTime, startupDelay, settings.CheckInterval);
        try
        {
            await Task.Delay(startupDelay, timeProvider, stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunCycleAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(options.Value.Containers.CheckInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(EventId = 15000, Level = LogLevel.Information, Message = "Container-Bereinigung (laufend länger als {MaxRunTime} oder liegengeblieben) startet in {StartupDelay} (Intervall {Interval})")]
    private static partial void LogStarted(ILogger logger, TimeSpan maxRunTime, TimeSpan startupDelay, TimeSpan interval);

    [LoggerMessage(EventId = 15001, Level = LogLevel.Information, Message = "Container-Bereinigung ist deaktiviert")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 15002, Level = LogLevel.Error, Message = "Container-Bereinigung fehlgeschlagen")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception);
}
