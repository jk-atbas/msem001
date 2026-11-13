using Msem001.Watchdog.Monitoring;

namespace Msem001.Watchdog.Tests.Monitoring;

public sealed class UsageAlertTrackerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 13, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Repeat = TimeSpan.FromHours(6);

    private readonly UsageAlertTracker tracker = new();

    [Test]
    [Arguments(50, (int)UsageLevel.Normal)]
    [Arguments(90, (int)UsageLevel.Normal)]
    [Arguments(90.1, (int)UsageLevel.High)]
    [Arguments(95, (int)UsageLevel.High)]
    [Arguments(95.1, (int)UsageLevel.Critical)]
    public async Task Classify_UsesStrictThresholds(double usedPercent, int expected)
    {
        await Assert.That(UsageAlertTracker.Classify(usedPercent, 90, 95)).IsEqualTo((UsageLevel)expected);
    }

    [Test]
    public async Task Track_Normal_ReportsNothing()
    {
        await Assert.That(tracker.Track(UsageLevel.Normal, Start, Repeat)).IsEqualTo(UsageAlertDecision.None);
    }

    [Test]
    public async Task Track_FirstHigh_Alerts_ThenStaysQuietUntilRepeatInterval()
    {
        await Assert.That(tracker.Track(UsageLevel.High, Start, Repeat)).IsEqualTo(UsageAlertDecision.Alert);
        await Assert.That(tracker.Track(UsageLevel.High, Start.AddHours(1), Repeat)).IsEqualTo(UsageAlertDecision.Unchanged);
        await Assert.That(tracker.Track(UsageLevel.High, Start.AddHours(6), Repeat)).IsEqualTo(UsageAlertDecision.Alert);
    }

    [Test]
    public async Task Track_Escalation_AlertsImmediately()
    {
        tracker.Track(UsageLevel.High, Start, Repeat);

        await Assert.That(tracker.Track(UsageLevel.Critical, Start.AddMinutes(1), Repeat)).IsEqualTo(UsageAlertDecision.Alert);
    }

    [Test]
    public async Task Track_Improvement_AndRecovery_AreReportedWithoutAlert()
    {
        tracker.Track(UsageLevel.Critical, Start, Repeat);

        await Assert.That(tracker.Track(UsageLevel.High, Start.AddMinutes(1), Repeat)).IsEqualTo(UsageAlertDecision.Improved);
        await Assert.That(tracker.Track(UsageLevel.Normal, Start.AddMinutes(2), Repeat)).IsEqualTo(UsageAlertDecision.Recovered);
        await Assert.That(tracker.Track(UsageLevel.High, Start.AddMinutes(3), Repeat)).IsEqualTo(UsageAlertDecision.Alert);
    }
}
