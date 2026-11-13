using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Monitoring;

namespace Msem001.Watchdog.Disk;

/// <summary>
/// Reports a high disk usage: above <see cref="DiskOptions.ErrorUsagePercent"/> as <c>Error</c>,
/// above <see cref="DiskOptions.CriticalUsagePercent"/> as <c>Critical</c> (both are mailed).
/// A persisting state is reported again after <see cref="DiskOptions.UsageAlertRepeatInterval"/>,
/// an escalation immediately.
/// </summary>
internal sealed partial class DiskUsageAlertEvaluator(
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<DiskUsageAlertEvaluator> logger)
{
    private readonly Lock sync = new();
    private readonly UsageAlertTracker tracker = new();

    public UsageLevel Evaluate(DiskSpaceInfo disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        var settings = options.Value.Disk;
        var level = UsageAlertTracker.Classify(disk.UsedPercent, settings.ErrorUsagePercent, settings.CriticalUsagePercent);

        UsageAlertDecision decision;
        lock (sync)
            decision = tracker.Track(level, timeProvider.GetUtcNow(), settings.UsageAlertRepeatInterval);

        switch (decision)
        {
            case UsageAlertDecision.Alert when level == UsageLevel.Critical:
                LogUsageCritical(logger, disk.Drive, disk.UsedPercent, disk.FreeGigabytes, settings.CriticalUsagePercent);
                break;
            case UsageAlertDecision.Alert:
                LogUsageHigh(logger, disk.Drive, disk.UsedPercent, disk.FreeGigabytes, settings.ErrorUsagePercent);
                break;
            case UsageAlertDecision.Recovered:
                LogUsageNormal(logger, disk.Drive, disk.UsedPercent, disk.FreeGigabytes);
                break;
            case UsageAlertDecision.Improved:
                LogUsageImproved(logger, disk.Drive, disk.UsedPercent, disk.FreeGigabytes);
                break;
            case UsageAlertDecision.Unchanged:
                LogUsageUnchanged(logger, disk.Drive, disk.UsedPercent, disk.FreeGigabytes);
                break;
        }

        return level;
    }

    [LoggerMessage(EventId = 1100, Level = LogLevel.Critical, Message = "Speicherplatz kritisch: {Drive} zu {UsedPercent:F1} % belegt (Schwelle {ThresholdPercent} %), nur noch {FreeGb:F1} GB frei")]
    private static partial void LogUsageCritical(ILogger logger, string drive, double usedPercent, double freeGb, double thresholdPercent);

    [LoggerMessage(EventId = 1101, Level = LogLevel.Error, Message = "Speicherplatz knapp: {Drive} zu {UsedPercent:F1} % belegt (Schwelle {ThresholdPercent} %), nur noch {FreeGb:F1} GB frei")]
    private static partial void LogUsageHigh(ILogger logger, string drive, double usedPercent, double freeGb, double thresholdPercent);

    [LoggerMessage(EventId = 1102, Level = LogLevel.Information, Message = "Speicherbelegung wieder im Normalbereich: {Drive} zu {UsedPercent:F1} % belegt, {FreeGb:F1} GB frei")]
    private static partial void LogUsageNormal(ILogger logger, string drive, double usedPercent, double freeGb);

    [LoggerMessage(EventId = 1103, Level = LogLevel.Information, Message = "Speicherbelegung verbessert, aber weiterhin hoch: {Drive} zu {UsedPercent:F1} % belegt, {FreeGb:F1} GB frei")]
    private static partial void LogUsageImproved(ILogger logger, string drive, double usedPercent, double freeGb);

    [LoggerMessage(EventId = 1104, Level = LogLevel.Debug, Message = "Speicherbelegung unverändert hoch (bereits gemeldet): {Drive} zu {UsedPercent:F1} % belegt, {FreeGb:F1} GB frei")]
    private static partial void LogUsageUnchanged(ILogger logger, string drive, double usedPercent, double freeGb);
}
