using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;

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
    private DiskUsageLevel lastLevel = DiskUsageLevel.Normal;
    private DateTimeOffset? lastAlertAt;

    public DiskUsageLevel Evaluate(DiskSpaceInfo disk)
    {
        ArgumentNullException.ThrowIfNull(disk);

        var settings = options.Value.Disk;
        var level = Classify(disk.UsedPercent, settings);
        var now = timeProvider.GetUtcNow();

        lock (sync)
        {
            var previous = lastLevel;
            lastLevel = level;

            if (level == DiskUsageLevel.Normal)
            {
                if (previous != DiskUsageLevel.Normal)
                    LogUsageNormal(logger, disk.Drive, disk.UsedPercent, disk.FreeGigabytes);

                lastAlertAt = null;
                return level;
            }

            var escalated = level > previous;
            var repeatDue = lastAlertAt is not { } last || now - last >= settings.UsageAlertRepeatInterval;
            if (!escalated && !repeatDue)
            {
                if (level < previous)
                    LogUsageImproved(logger, disk.Drive, disk.UsedPercent, disk.FreeGigabytes);
                else
                    LogUsageUnchanged(logger, disk.Drive, disk.UsedPercent, disk.FreeGigabytes);

                return level;
            }

            if (level == DiskUsageLevel.Critical)
                LogUsageCritical(logger, disk.Drive, disk.UsedPercent, disk.FreeGigabytes, settings.CriticalUsagePercent);
            else
                LogUsageHigh(logger, disk.Drive, disk.UsedPercent, disk.FreeGigabytes, settings.ErrorUsagePercent);

            lastAlertAt = now;
            return level;
        }
    }

    internal static DiskUsageLevel Classify(double usedPercent, DiskOptions settings)
    {
        if (usedPercent > settings.CriticalUsagePercent)
            return DiskUsageLevel.Critical;

        return usedPercent > settings.ErrorUsagePercent ? DiskUsageLevel.High : DiskUsageLevel.Normal;
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
