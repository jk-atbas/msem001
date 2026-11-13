using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Maintenance;

namespace Msem001.Watchdog.Workers;

/// <summary>
/// Repeats the disk maintenance check daily before the nightly Jenkins job (default 20:45, job at 22:00).
/// </summary>
internal sealed partial class NightlyPreCheckWorker(
    DiskMaintenanceWorkflow maintenanceWorkflow,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<NightlyPreCheckWorker> logger) : BackgroundService
{
    internal async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            LogRunning(logger);
            var outcome = await maintenanceWorkflow.RunAsync(MaintenanceTrigger.Nightly, cancellationToken).ConfigureAwait(false);
            LogFinished(logger, outcome);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogFailed(logger, ex);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var next = DailySchedule.GetNextOccurrence(
                    timeProvider.GetUtcNow(),
                    options.Value.Schedule.NightlyPreCheckTime,
                    timeProvider.LocalTimeZone);

                var nextLocal = TimeZoneInfo.ConvertTime(next, timeProvider.LocalTimeZone);
                LogScheduled(logger, nextLocal);
                await timeProvider.DelayUntilAsync(next, stoppingToken).ConfigureAwait(false);
                await RunCycleAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(EventId = 11000, Level = LogLevel.Information, Message = "Nächste Nightly-Vorabprüfung: {NextRun}")]
    private static partial void LogScheduled(ILogger logger, DateTimeOffset nextRun);

    [LoggerMessage(EventId = 11001, Level = LogLevel.Information, Message = "Nightly-Vorabprüfung des Speicherplatzes startet")]
    private static partial void LogRunning(ILogger logger);

    [LoggerMessage(EventId = 11002, Level = LogLevel.Information, Message = "Nightly-Vorabprüfung abgeschlossen: {Outcome}")]
    private static partial void LogFinished(ILogger logger, DiskMaintenanceOutcome outcome);

    [LoggerMessage(EventId = 11003, Level = LogLevel.Error, Message = "Nightly-Vorabprüfung fehlgeschlagen")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
