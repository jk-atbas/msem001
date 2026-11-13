using System.ComponentModel;
using Microsoft.Extensions.Time.Testing;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Memory;
using Msem001.Watchdog.Tests.TestSupport;
using Msem001.Watchdog.Workers;

namespace Msem001.Watchdog.Tests.Workers;

public sealed class MemoryMonitorWorkerTests
{
    private readonly Mock<IMemoryProbe> probe = Mock.Of<IMemoryProbe>();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));
    private readonly MockLogger<MemoryUsageAlertEvaluator> evaluatorLogger = Mock.Logger<MemoryUsageAlertEvaluator>();
    private readonly MockLogger<MemoryMonitorWorker> logger = Mock.Logger<MemoryMonitorWorker>();
    private int reads;

    public MemoryMonitorWorkerTests()
    {
        probe.GetMemory().Returns(TestData.Memory(usedPercent: 97)).Callback(() => Interlocked.Increment(ref reads));
    }

    [Test]
    public async Task RunCycle_EvaluatesCurrentUsage()
    {
        var worker = CreateSut(o => o.Memory.SustainedDuration = TimeSpan.Zero);

        worker.RunCycle();

        evaluatorLogger.VerifyLog().AtLevel(LogLevel.Critical).ContainingMessage("97.0 % belegt").WasCalled(Times.Once);
        await Assert.That(reads).IsEqualTo(1);
    }

    [Test]
    public async Task RunCycle_ProbeFails_LogsError()
    {
        probe.GetMemory().Throws(new Win32Exception(5));
        var worker = CreateSut();

        worker.RunCycle();

        logger.VerifyLog().AtLevel(LogLevel.Error).WithException<Win32Exception>().WasCalled(Times.Once);
        await Assert.That(evaluatorLogger.Entries).IsEmpty();
    }

    [Test]
    public async Task ExecuteAsync_ChecksCyclically_AndReportsSustainedUsage()
    {
        using var worker = CreateSut();

        await worker.StartAsync(TestData.Ct);
        await Wait.UntilAsync(() => evaluatorLogger.Entries.Any(e => e.LogLevel == LogLevel.Critical), time, TimeSpan.FromMinutes(1));
        await worker.StopAsync(TestData.Ct);

        await Assert.That(Volatile.Read(ref reads)).IsGreaterThanOrEqualTo(6);
    }

    [Test]
    public async Task ExecuteAsync_Disabled_DoesNotCheck()
    {
        using var worker = CreateSut(o => o.Memory.Enabled = false);

        await worker.StartAsync(TestData.Ct);
        time.Advance(TimeSpan.FromMinutes(10));
        await worker.StopAsync(TestData.Ct);

        probe.GetMemory().WasNeverCalled();
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("deaktiviert").WasCalled(Times.Once);
    }

    private MemoryMonitorWorker CreateSut(Action<WatchdogOptions>? configure = null)
    {
        var options = TestData.CreateOptions(configure);
        return new MemoryMonitorWorker(probe.Object, new MemoryUsageAlertEvaluator(options, time, evaluatorLogger), options, time, logger);
    }
}
