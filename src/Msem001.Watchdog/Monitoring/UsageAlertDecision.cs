namespace Msem001.Watchdog.Monitoring;

/// <summary>
/// What an evaluator has to report for the current usage sample.
/// </summary>
internal enum UsageAlertDecision
{
    /// <summary>Normal usage, nothing to report.</summary>
    None = 0,

    /// <summary>First sample in the alert state, an escalation, or the repeat interval elapsed: report as Error/Critical.</summary>
    Alert = 1,

    /// <summary>Back to normal after an alert.</summary>
    Recovered = 2,

    /// <summary>Still high but lower than before (Critical to High), already reported.</summary>
    Improved = 3,

    /// <summary>Still high on the same level, already reported.</summary>
    Unchanged = 4,
}
