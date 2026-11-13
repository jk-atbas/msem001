namespace Msem001.Watchdog.Monitoring;

/// <summary>
/// Alert state machine shared by the disk and RAM checks: an escalation is reported immediately,
/// a persisting level again after the repeat interval, an improvement or recovery once without alert.
/// </summary>
/// <remarks>Not thread-safe; the owning evaluator synchronizes the calls.</remarks>
internal sealed class UsageAlertTracker
{
    private UsageLevel lastLevel = UsageLevel.Normal;
    private DateTimeOffset? lastAlertAt;

    /// <summary>Classifies a usage strictly above the thresholds (exactly 90 % is still normal).</summary>
    /// <returns>The usage level.</returns>
    public static UsageLevel Classify(double usedPercent, double errorPercent, double criticalPercent)
    {
        if (usedPercent > criticalPercent)
            return UsageLevel.Critical;

        return usedPercent > errorPercent ? UsageLevel.High : UsageLevel.Normal;
    }

    /// <summary>Records the level of the current sample.</summary>
    /// <returns>What has to be reported.</returns>
    public UsageAlertDecision Track(UsageLevel level, DateTimeOffset now, TimeSpan repeatInterval)
    {
        var previous = lastLevel;
        lastLevel = level;

        if (level == UsageLevel.Normal)
        {
            lastAlertAt = null;
            return previous == UsageLevel.Normal ? UsageAlertDecision.None : UsageAlertDecision.Recovered;
        }

        var escalated = level > previous;
        var repeatDue = lastAlertAt is not { } last || now - last >= repeatInterval;
        if (!escalated && !repeatDue)
            return level < previous ? UsageAlertDecision.Improved : UsageAlertDecision.Unchanged;

        lastAlertAt = now;
        return UsageAlertDecision.Alert;
    }
}
