using Microsoft.Extensions.Time.Testing;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Disk;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Disk;

public sealed class DiskUsageAlertEvaluatorTests
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));
    private readonly MockLogger<DiskUsageAlertEvaluator> logger = Mock.Logger<DiskUsageAlertEvaluator>();
    private readonly DiskUsageAlertEvaluator evaluator;

    public DiskUsageAlertEvaluatorTests()
    {
        evaluator = new DiskUsageAlertEvaluator(TestData.CreateOptions(), time, logger);
    }

    [Test]
    [Arguments(50, (int)DiskUsageLevel.Normal)]
    [Arguments(90, (int)DiskUsageLevel.Normal)]
    [Arguments(90.1, (int)DiskUsageLevel.High)]
    [Arguments(95, (int)DiskUsageLevel.High)]
    [Arguments(95.1, (int)DiskUsageLevel.Critical)]
    public async Task Classify_UsesStrictThresholds(double usedPercent, int expected)
    {
        await Assert.That(DiskUsageAlertEvaluator.Classify(usedPercent, new DiskOptions())).IsEqualTo((DiskUsageLevel)expected);
    }

    [Test]
    public async Task Evaluate_NormalUsage_LogsNothingRelevant()
    {
        await Assert.That(evaluator.Evaluate(TestData.Disk(freeGb: 500))).IsEqualTo(DiskUsageLevel.Normal);

        await Assert.That(logger.CountAtLeast(LogLevel.Warning)).IsEqualTo(0);
    }

    [Test]
    public async Task Evaluate_Above90Percent_LogsErrorWithFreeSpace()
    {
        await Assert.That(evaluator.Evaluate(TestData.Disk(freeGb: 80))).IsEqualTo(DiskUsageLevel.High);

        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("92.0 %").WasCalled(Times.Once);
        await Assert.That(logger.Latest().Message).Contains("80.0 GB frei");
    }

    [Test]
    public async Task Evaluate_Above95Percent_LogsCritical()
    {
        await Assert.That(evaluator.Evaluate(TestData.Disk(freeGb: 30))).IsEqualTo(DiskUsageLevel.Critical);

        logger.VerifyLog().AtLevel(LogLevel.Critical).ContainingMessage("30.0 GB frei").WasCalled(Times.Once);
    }

    [Test]
    public async Task Evaluate_PersistingHighUsage_IsReportedAgainOnlyAfterRepeatInterval()
    {
        evaluator.Evaluate(TestData.Disk(freeGb: 80));
        time.Advance(TimeSpan.FromHours(1));
        evaluator.Evaluate(TestData.Disk(freeGb: 79));

        await Assert.That(logger.CountAtLeast(LogLevel.Error)).IsEqualTo(1);

        time.Advance(TimeSpan.FromHours(6));
        evaluator.Evaluate(TestData.Disk(freeGb: 78));

        await Assert.That(logger.CountAtLeast(LogLevel.Error)).IsEqualTo(2);
    }

    [Test]
    public async Task Evaluate_Escalation_IsReportedImmediately()
    {
        evaluator.Evaluate(TestData.Disk(freeGb: 80));
        time.Advance(TimeSpan.FromMinutes(5));
        evaluator.Evaluate(TestData.Disk(freeGb: 40));

        var alertLevels = logger.Entries.Where(e => e.LogLevel >= LogLevel.Error).Select(e => e.LogLevel);
        await Assert.That(string.Join(",", alertLevels)).IsEqualTo("Error,Critical");
    }

    [Test]
    public async Task Evaluate_Improvement_IsLoggedAsInformationWithoutAlert()
    {
        evaluator.Evaluate(TestData.Disk(freeGb: 40));
        time.Advance(TimeSpan.FromMinutes(5));

        await Assert.That(evaluator.Evaluate(TestData.Disk(freeGb: 80))).IsEqualTo(DiskUsageLevel.High);

        await Assert.That(logger.CountAtLeast(LogLevel.Error)).IsEqualTo(1);
        await Assert.That(logger.Latest().LogLevel).IsEqualTo(LogLevel.Information);
    }

    [Test]
    public async Task Evaluate_BackToNormal_LogsRecoveryAndResetsState()
    {
        evaluator.Evaluate(TestData.Disk(freeGb: 80));
        evaluator.Evaluate(TestData.Disk(freeGb: 500));

        await Assert.That(logger.Latest().LogLevel).IsEqualTo(LogLevel.Information);
        await Assert.That(logger.Latest().Message).Contains("Normalbereich");

        evaluator.Evaluate(TestData.Disk(freeGb: 80));
        await Assert.That(logger.CountAtLeast(LogLevel.Error)).IsEqualTo(2);
    }
}
