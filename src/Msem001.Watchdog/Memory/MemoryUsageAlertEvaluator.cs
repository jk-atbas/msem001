using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Monitoring;

namespace Msem001.Watchdog.Memory;

/// <summary>
/// Reports a high RAM usage: above <see cref="MemoryOptions.ErrorUsagePercent"/> as <c>Error</c>,
/// above <see cref="MemoryOptions.CriticalUsagePercent"/> as <c>Critical</c> (both are mailed), analogous to the disk check.
/// </summary>
/// <remarks>
/// Unlike the disk, RAM usage peaks during builds. A level is only reported once its threshold has been exceeded
/// continuously for <see cref="MemoryOptions.SustainedDuration"/>; a single sample below the threshold resets the timer.
/// </remarks>
internal sealed partial class MemoryUsageAlertEvaluator(
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<MemoryUsageAlertEvaluator> logger)
{
    private readonly Lock sync = new();
    private readonly UsageAlertTracker tracker = new();
    private DateTimeOffset? highSince;
    private DateTimeOffset? criticalSince;

    /// <summary>Evaluates a sample.</summary>
    /// <returns>The reported (sustained) level.</returns>
    public UsageLevel Evaluate(MemoryInfo memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var settings = options.Value.Memory;
        var now = timeProvider.GetUtcNow();
        var current = UsageAlertTracker.Classify(memory.UsedPercent, settings.ErrorUsagePercent, settings.CriticalUsagePercent);

        UsageLevel sustained;
        UsageAlertDecision decision;
        lock (sync)
        {
            highSince = current >= UsageLevel.High ? highSince ?? now : null;
            criticalSince = current == UsageLevel.Critical ? criticalSince ?? now : null;
            sustained = SustainedLevel(now, settings.SustainedDuration);
            decision = tracker.Track(sustained, now, settings.UsageAlertRepeatInterval);
        }

        switch (decision)
        {
            case UsageAlertDecision.Alert when sustained == UsageLevel.Critical:
                LogUsageCritical(logger, memory.UsedPercent, memory.AvailableGigabytes, memory.TotalGigabytes, settings.CriticalUsagePercent, settings.SustainedDuration);
                break;
            case UsageAlertDecision.Alert:
                LogUsageHigh(logger, memory.UsedPercent, memory.AvailableGigabytes, memory.TotalGigabytes, settings.ErrorUsagePercent, settings.SustainedDuration);
                break;
            case UsageAlertDecision.Recovered:
                LogUsageNormal(logger, memory.UsedPercent, memory.AvailableGigabytes);
                break;
            case UsageAlertDecision.Improved:
                LogUsageImproved(logger, memory.UsedPercent, memory.AvailableGigabytes);
                break;
            case UsageAlertDecision.Unchanged:
                LogUsageUnchanged(logger, memory.UsedPercent, memory.AvailableGigabytes);
                break;
        }

        if (current > sustained)
            LogPeakPending(logger, memory.UsedPercent, settings.SustainedDuration);

        return sustained;
    }

    [LoggerMessage(EventId = 1200, Level = LogLevel.Critical, Message = "RAM-Auslastung kritisch: {UsedPercent:F1} % belegt (Schwelle {ThresholdPercent} %, seit mindestens {SustainedDuration}), nur noch {AvailableGb:F1} GB von {TotalGb:F1} GB frei")]
    private static partial void LogUsageCritical(ILogger logger, double usedPercent, double availableGb, double totalGb, double thresholdPercent, TimeSpan sustainedDuration);

    [LoggerMessage(EventId = 1201, Level = LogLevel.Error, Message = "RAM-Auslastung hoch: {UsedPercent:F1} % belegt (Schwelle {ThresholdPercent} %, seit mindestens {SustainedDuration}), nur noch {AvailableGb:F1} GB von {TotalGb:F1} GB frei")]
    private static partial void LogUsageHigh(ILogger logger, double usedPercent, double availableGb, double totalGb, double thresholdPercent, TimeSpan sustainedDuration);

    [LoggerMessage(EventId = 1202, Level = LogLevel.Information, Message = "RAM-Auslastung wieder im Normalbereich: {UsedPercent:F1} % belegt, {AvailableGb:F1} GB frei")]
    private static partial void LogUsageNormal(ILogger logger, double usedPercent, double availableGb);

    [LoggerMessage(EventId = 1203, Level = LogLevel.Information, Message = "RAM-Auslastung verbessert, aber weiterhin hoch: {UsedPercent:F1} % belegt, {AvailableGb:F1} GB frei")]
    private static partial void LogUsageImproved(ILogger logger, double usedPercent, double availableGb);

    [LoggerMessage(EventId = 1204, Level = LogLevel.Debug, Message = "RAM-Auslastung unverändert hoch (bereits gemeldet): {UsedPercent:F1} % belegt, {AvailableGb:F1} GB frei")]
    private static partial void LogUsageUnchanged(ILogger logger, double usedPercent, double availableGb);

    [LoggerMessage(EventId = 1205, Level = LogLevel.Debug, Message = "RAM-Auslastung {UsedPercent:F1} % über Schwelle, wird erst nach {SustainedDuration} am Stück gemeldet")]
    private static partial void LogPeakPending(ILogger logger, double usedPercent, TimeSpan sustainedDuration);

    private UsageLevel SustainedLevel(DateTimeOffset now, TimeSpan sustainedDuration)
    {
        if (criticalSince is { } critical && now - critical >= sustainedDuration)
            return UsageLevel.Critical;

        return highSince is { } high && now - high >= sustainedDuration ? UsageLevel.High : UsageLevel.Normal;
    }
}
