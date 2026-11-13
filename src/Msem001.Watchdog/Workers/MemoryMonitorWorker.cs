using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Memory;

namespace Msem001.Watchdog.Workers;

/// <summary>
/// Cyclic RAM check: usage alerts (&gt;90 % / &gt;95 %, sustained).
/// </summary>
internal sealed partial class MemoryMonitorWorker(
    IMemoryProbe memoryProbe,
    MemoryUsageAlertEvaluator usageEvaluator,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<MemoryMonitorWorker> logger) : BackgroundService
{
    internal void RunCycle()
    {
        try
        {
            var memory = memoryProbe.GetMemory();
            LogMemoryStatus(logger, memory.UsedPercent, memory.AvailableGigabytes, memory.TotalGigabytes);
            usageEvaluator.Evaluate(memory);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogCycleFailed(logger, ex);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value.Memory;
        if (!settings.Enabled)
        {
            LogDisabled(logger);
            return;
        }

        LogStarted(logger, settings.CheckInterval, settings.SustainedDuration);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                RunCycle();
                await Task.Delay(options.Value.Memory.CheckInterval, timeProvider, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    [LoggerMessage(EventId = 13000, Level = LogLevel.Information, Message = "RAM-Überwachung gestartet (Intervall {Interval}, Meldung nach {SustainedDuration} am Stück)")]
    private static partial void LogStarted(ILogger logger, TimeSpan interval, TimeSpan sustainedDuration);

    [LoggerMessage(EventId = 13001, Level = LogLevel.Information, Message = "RAM-Überwachung ist deaktiviert")]
    private static partial void LogDisabled(ILogger logger);

    [LoggerMessage(EventId = 13002, Level = LogLevel.Debug, Message = "RAM-Status: {UsedPercent:F1} % belegt, {AvailableGb:F1} GB frei von {TotalGb:F1} GB")]
    private static partial void LogMemoryStatus(ILogger logger, double usedPercent, double availableGb, double totalGb);

    [LoggerMessage(EventId = 13003, Level = LogLevel.Error, Message = "RAM-Prüfung fehlgeschlagen")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception);
}
