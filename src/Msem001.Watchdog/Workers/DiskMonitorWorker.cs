using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Disk;
using Msem001.Watchdog.Maintenance;

namespace Msem001.Watchdog.Workers;

/// <summary>
/// Cyclic disk check: usage alerts (&gt;90 % / &gt;95 %) and disk maintenance below 50 GB.
/// </summary>
internal sealed partial class DiskMonitorWorker(
    IDiskSpaceProbe diskSpaceProbe,
    DiskUsageAlertEvaluator usageEvaluator,
    DiskMaintenanceWorkflow maintenanceWorkflow,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<DiskMonitorWorker> logger) : BackgroundService
{
    internal async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        try
        {
            var disk = diskSpaceProbe.GetDiskSpace();
            LogDiskStatus(logger, disk.Drive, disk.FreeGigabytes, disk.TotalGigabytes, disk.UsedPercent);
            usageEvaluator.Evaluate(disk);

            await maintenanceWorkflow.RunAsync(MaintenanceTrigger.Periodic, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCycleFailed(logger, ex);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        LogStarted(logger, options.Value.Disk.CheckInterval);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunCycleAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(options.Value.Disk.CheckInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(EventId = 10000, Level = LogLevel.Information, Message = "Speicherüberwachung gestartet (Intervall {Interval})")]
    private static partial void LogStarted(ILogger logger, TimeSpan interval);

    [LoggerMessage(EventId = 10001, Level = LogLevel.Debug, Message = "Speicherstatus {Drive}: {FreeGb:F1} GB frei von {TotalGb:F1} GB ({UsedPercent:F1} % belegt)")]
    private static partial void LogDiskStatus(ILogger logger, string drive, double freeGb, double totalGb, double usedPercent);

    [LoggerMessage(EventId = 10002, Level = LogLevel.Error, Message = "Speicherprüfung fehlgeschlagen")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception);
}
