using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Disk;
using Msem001.Watchdog.Jenkins;
using Msem001.Watchdog.Maintenance;
using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Scripts;
using Msem001.Watchdog.Tests.TestSupport;
using Msem001.Watchdog.Workers;

namespace Msem001.Watchdog.Tests.Workers;

/// <summary>
/// The workers are thin loops; they are tested with the real workflows on top of mocked infrastructure and a fake clock.
/// </summary>
public sealed class WorkerTests : IDisposable
{
    private readonly Mock<IDiskSpaceProbe> disk = Mock.Of<IDiskSpaceProbe>();
    private readonly Mock<IMaintenanceScripts> scripts = Mock.Of<IMaintenanceScripts>();
    private readonly Mock<IPodmanHealthChecker> health = Mock.Of<IPodmanHealthChecker>();
    private readonly Mock<IPodmanMachine> machine = Mock.Of<IPodmanMachine>();
    private readonly Mock<IJenkinsAgent> agent = Mock.Of<IJenkinsAgent>();
    private readonly Mock<IJenkinsAgentDrain> drain = Mock.Of<IJenkinsAgentDrain>();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 13, 20, 40, 0, TimeSpan.Zero));
    private readonly IOptions<WatchdogOptions> options = TestData.CreateOptions();
    private readonly MaintenanceExecutor executor;
    private int diskReads;
    private int cleanups;
    private int agentStarts;

    public WorkerTests()
    {
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        scripts.RunJenkinsWorkspaceCleanupAsync(Arg.Any<CancellationToken>()).Returns(TestData.Success()).Callback(() => Interlocked.Increment(ref cleanups));
        scripts.RunPodmanResetAsync(Arg.Any<CancellationToken>()).Returns(TestData.Success());
        health.CheckAsync(Arg.Any<CancellationToken>()).Returns(TestData.Healthy());
        agent.IsRunning().Returns(true);
        agent.StartAsync(Arg.Any<CancellationToken>()).Callback(() => Interlocked.Increment(ref agentStarts));
        executor = new MaintenanceExecutor(agent.Object, drain.Object, Mock.Logger<MaintenanceExecutor>());
    }

    public void Dispose() => executor.Dispose();

    [Test]
    public async Task DiskMonitorWorker_RunCycle_ReportsUsageAndRunsMaintenance()
    {
        disk.GetDiskSpace().Returns(TestData.Disk(freeGb: 30));
        var evaluatorLogger = Mock.Logger<DiskUsageAlertEvaluator>();
        var worker = new DiskMonitorWorker(disk.Object, new DiskUsageAlertEvaluator(options, time, evaluatorLogger), CreateWorkflow(), options, time, Mock.Logger<DiskMonitorWorker>());

        await worker.RunCycleAsync(TestData.Ct);

        evaluatorLogger.VerifyLog().AtLevel(LogLevel.Critical).WasCalled(Times.Once);
        await Assert.That(cleanups).IsEqualTo(1);
    }

    [Test]
    public async Task DiskMonitorWorker_RunCycle_ProbeFails_LogsError()
    {
        disk.GetDiskSpace().Throws(new IOException("kaputt"));
        var logger = Mock.Logger<DiskMonitorWorker>();
        var worker = new DiskMonitorWorker(disk.Object, new DiskUsageAlertEvaluator(options, time, Mock.Logger<DiskUsageAlertEvaluator>()), CreateWorkflow(), options, time, logger);

        await worker.RunCycleAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Error).WithException<IOException>().WasCalled(Times.Once);
    }

    [Test]
    public async Task DiskMonitorWorker_ExecuteAsync_ChecksCyclically()
    {
        disk.GetDiskSpace().Returns(TestData.Disk(freeGb: 500)).Callback(() => Interlocked.Increment(ref diskReads));
        using var worker = new DiskMonitorWorker(disk.Object, new DiskUsageAlertEvaluator(options, time, Mock.Logger<DiskUsageAlertEvaluator>()), CreateWorkflow(), options, time, Mock.Logger<DiskMonitorWorker>());

        await worker.StartAsync(TestData.Ct);
        await Wait.UntilAsync(() => Volatile.Read(ref diskReads) >= 2);

        // Each cycle reads the disk twice (usage alert + maintenance check); the next cycle needs the 5 minute interval.
        await Wait.UntilAsync(() => Volatile.Read(ref diskReads) >= 4, time, TimeSpan.FromMinutes(1));
        await worker.StopAsync(TestData.Ct);
    }

    [Test]
    public async Task NightlyPreCheckWorker_RunsAtConfiguredTime_IgnoringCooldown()
    {
        disk.GetDiskSpace().Returns(TestData.Disk(freeGb: 30));
        var workflow = CreateWorkflow();
        await workflow.RunAsync(MaintenanceTrigger.Periodic, TestData.Ct);
        await Assert.That(cleanups).IsEqualTo(1);

        using var worker = new NightlyPreCheckWorker(workflow, options, time, Mock.Logger<NightlyPreCheckWorker>());
        await worker.StartAsync(TestData.Ct);
        await Task.Delay(50, TestData.Ct);
        await Assert.That(cleanups).IsEqualTo(1);

        await Wait.UntilAsync(() => Volatile.Read(ref cleanups) == 2, time, TimeSpan.FromMinutes(1));
        await worker.StopAsync(TestData.Ct);

        await Assert.That(time.GetLocalNow().TimeOfDay).IsGreaterThanOrEqualTo(new TimeSpan(20, 45, 0));
    }

    [Test]
    public async Task NightlyPreCheckWorker_RunCycle_FailureIsLogged()
    {
        disk.GetDiskSpace().Throws(new IOException("kaputt"));
        var logger = Mock.Logger<NightlyPreCheckWorker>();
        var worker = new NightlyPreCheckWorker(CreateWorkflow(), options, time, logger);

        await worker.RunCycleAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Error).WithException<IOException>().WasCalled(Times.Once);
    }

    [Test]
    public async Task BuildAgentHealthWorker_WaitsForStartupDelayThenKeepsAgentRunning()
    {
        agent.IsRunning().Returns(false);
        using var worker = new BuildAgentHealthWorker(CreateMonitor(), options, time, Mock.Logger<BuildAgentHealthWorker>());

        await worker.StartAsync(TestData.Ct);
        await Task.Delay(50, TestData.Ct);
        health.CheckAsync(Arg.Any<CancellationToken>()).WasNeverCalled();

        await Wait.UntilAsync(() => Volatile.Read(ref agentStarts) >= 1, time, TimeSpan.FromSeconds(10));
        await worker.StopAsync(TestData.Ct);
    }

    [Test]
    public async Task BuildAgentHealthWorker_RunCycle_FailureIsLogged()
    {
        health.CheckAsync(Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("wmi"));
        var logger = Mock.Logger<BuildAgentHealthWorker>();
        var worker = new BuildAgentHealthWorker(CreateMonitor(), options, time, logger);

        await worker.RunCycleAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Error).WithException<InvalidOperationException>().WasCalled(Times.Once);
    }

    private PodmanRecovery CreateRecovery() =>
        new(health.Object, machine.Object, scripts.Object, options, time, Mock.Logger<PodmanRecovery>());

    private DiskMaintenanceWorkflow CreateWorkflow() =>
        new(disk.Object, scripts.Object, CreateRecovery(), executor, options, time, Mock.Logger<DiskMaintenanceWorkflow>());

    private BuildAgentHealthMonitor CreateMonitor() =>
        new(health.Object, CreateRecovery(), executor, options, time, Mock.Logger<BuildAgentHealthMonitor>());
}
