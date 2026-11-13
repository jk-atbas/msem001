using Microsoft.Extensions.Time.Testing;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Memory;
using Msem001.Watchdog.Monitoring;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Memory;

public sealed class MemoryUsageAlertEvaluatorTests
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));
    private readonly MockLogger<MemoryUsageAlertEvaluator> logger = Mock.Logger<MemoryUsageAlertEvaluator>();

    [Test]
    public async Task Evaluate_NormalUsage_LogsNothingRelevant()
    {
        var evaluator = CreateSut();

        await Assert.That(evaluator.Evaluate(TestData.Memory(usedPercent: 60))).IsEqualTo(UsageLevel.Normal);

        await Assert.That(logger.CountAtLeast(LogLevel.Warning)).IsEqualTo(0);
    }

    [Test]
    public async Task Evaluate_ShortPeakDuringBuild_IsNotReported()
    {
        var evaluator = CreateSut();

        evaluator.Evaluate(TestData.Memory(usedPercent: 97));
        time.Advance(TimeSpan.FromMinutes(2));
        evaluator.Evaluate(TestData.Memory(usedPercent: 98));
        time.Advance(TimeSpan.FromMinutes(1));
        evaluator.Evaluate(TestData.Memory(usedPercent: 70));

        await Assert.That(logger.CountAtLeast(LogLevel.Error)).IsEqualTo(0);
        logger.VerifyLog().AtLevel(LogLevel.Debug).ContainingMessage("erst nach").WasCalled(Times.Exactly(2));
    }

    [Test]
    public async Task Evaluate_Above90PercentForSustainedDuration_LogsErrorWithUsage()
    {
        var evaluator = CreateSut();

        await Assert.That(evaluator.Evaluate(TestData.Memory(usedPercent: 92))).IsEqualTo(UsageLevel.Normal);
        time.Advance(TimeSpan.FromMinutes(5));

        await Assert.That(evaluator.Evaluate(TestData.Memory(usedPercent: 92.5, totalGb: 32))).IsEqualTo(UsageLevel.High);
        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("RAM-Auslastung hoch: 92.5 % belegt").WasCalled(Times.Once);
        await Assert.That(logger.Latest().Message).Contains("2.4 GB von 32.0 GB frei");
    }

    [Test]
    public async Task Evaluate_Above95PercentForSustainedDuration_LogsCritical()
    {
        var evaluator = CreateSut();

        evaluator.Evaluate(TestData.Memory(usedPercent: 97));
        time.Advance(TimeSpan.FromMinutes(5));

        await Assert.That(evaluator.Evaluate(TestData.Memory(usedPercent: 96))).IsEqualTo(UsageLevel.Critical);
        logger.VerifyLog().AtLevel(LogLevel.Critical).ContainingMessage("96.0 % belegt").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Error).WasNeverCalled();
    }

    [Test]
    public async Task Evaluate_HighThenCritical_EscalatesOnlyAfterCriticalIsSustainedToo()
    {
        var evaluator = CreateSut();

        evaluator.Evaluate(TestData.Memory(usedPercent: 92));
        time.Advance(TimeSpan.FromMinutes(5));
        await Assert.That(evaluator.Evaluate(TestData.Memory(usedPercent: 96))).IsEqualTo(UsageLevel.High);

        time.Advance(TimeSpan.FromMinutes(4));
        await Assert.That(evaluator.Evaluate(TestData.Memory(usedPercent: 96))).IsEqualTo(UsageLevel.High);

        time.Advance(TimeSpan.FromMinutes(1));
        await Assert.That(evaluator.Evaluate(TestData.Memory(usedPercent: 96))).IsEqualTo(UsageLevel.Critical);

        var alertLevels = logger.Entries.Where(e => e.LogLevel >= LogLevel.Error).Select(e => e.LogLevel);
        await Assert.That(string.Join(",", alertLevels)).IsEqualTo("Error,Critical");
    }

    [Test]
    public async Task Evaluate_DipBelowThreshold_RestartsSustainTimer()
    {
        var evaluator = CreateSut();

        evaluator.Evaluate(TestData.Memory(usedPercent: 92));
        time.Advance(TimeSpan.FromMinutes(4));
        evaluator.Evaluate(TestData.Memory(usedPercent: 80));
        time.Advance(TimeSpan.FromMinutes(1));
        evaluator.Evaluate(TestData.Memory(usedPercent: 92));
        time.Advance(TimeSpan.FromMinutes(4));

        await Assert.That(evaluator.Evaluate(TestData.Memory(usedPercent: 92))).IsEqualTo(UsageLevel.Normal);
        await Assert.That(logger.CountAtLeast(LogLevel.Error)).IsEqualTo(0);
    }

    [Test]
    public async Task Evaluate_PersistingHighUsage_IsReportedAgainOnlyAfterRepeatInterval()
    {
        var evaluator = CreateSut(o => o.Memory.SustainedDuration = TimeSpan.Zero);

        evaluator.Evaluate(TestData.Memory(usedPercent: 92));
        time.Advance(TimeSpan.FromHours(1));
        evaluator.Evaluate(TestData.Memory(usedPercent: 93));
        await Assert.That(logger.CountAtLeast(LogLevel.Error)).IsEqualTo(1);

        time.Advance(TimeSpan.FromHours(6));
        evaluator.Evaluate(TestData.Memory(usedPercent: 93));
        await Assert.That(logger.CountAtLeast(LogLevel.Error)).IsEqualTo(2);
    }

    [Test]
    public async Task Evaluate_ImprovementAndRecovery_AreLoggedAsInformation()
    {
        var evaluator = CreateSut(o => o.Memory.SustainedDuration = TimeSpan.Zero);

        evaluator.Evaluate(TestData.Memory(usedPercent: 97));
        await Assert.That(evaluator.Evaluate(TestData.Memory(usedPercent: 92))).IsEqualTo(UsageLevel.High);
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("verbessert").WasCalled(Times.Once);

        await Assert.That(evaluator.Evaluate(TestData.Memory(usedPercent: 50))).IsEqualTo(UsageLevel.Normal);
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("wieder im Normalbereich").WasCalled(Times.Once);
        await Assert.That(logger.CountAtLeast(LogLevel.Error)).IsEqualTo(1);
    }

    private MemoryUsageAlertEvaluator CreateSut(Action<WatchdogOptions>? configure = null) =>
        new(TestData.CreateOptions(configure), time, logger);
}
