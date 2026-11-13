using Microsoft.Extensions.Time.Testing;
using Msem001.Watchdog.Disk;
using Msem001.Watchdog.Jenkins;
using Msem001.Watchdog.Maintenance;
using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Scripts;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Maintenance;

public sealed class DiskMaintenanceWorkflowTests : IDisposable
{
    private readonly Mock<IDiskSpaceProbe> disk = Mock.Of<IDiskSpaceProbe>();
    private readonly Mock<IMaintenanceScripts> scripts = Mock.Of<IMaintenanceScripts>();
    private readonly Mock<IPodmanHealthChecker> health = Mock.Of<IPodmanHealthChecker>();
    private readonly Mock<IPodmanMachine> machine = Mock.Of<IPodmanMachine>();
    private readonly Mock<IJenkinsAgent> agent = Mock.Of<IJenkinsAgent>();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));
    private readonly MockLogger<DiskMaintenanceWorkflow> logger = Mock.Logger<DiskMaintenanceWorkflow>();
    private readonly List<string> calls = [];
    private readonly MaintenanceExecutor executor;
    private readonly DiskMaintenanceWorkflow workflow;

    public DiskMaintenanceWorkflowTests()
    {
        var options = TestData.CreateOptions();
        scripts.RunJenkinsWorkspaceCleanupAsync(Arg.Any<CancellationToken>()).Returns(TestData.Success()).Callback(() => calls.Add("cleanup"));
        scripts.RunPodmanResetAsync(Arg.Any<CancellationToken>()).Returns(TestData.Success()).Callback(() => calls.Add("reset"));
        health.CheckAsync(Arg.Any<CancellationToken>()).Returns(TestData.Healthy());
        machine.StartAsync(Arg.Any<CancellationToken>()).Returns(true).Callback(() => calls.Add("machine start"));
        agent.StopAsync(Arg.Any<CancellationToken>()).Callback(() => calls.Add("agent stop"));
        agent.StartAsync(Arg.Any<CancellationToken>()).Callback(() => calls.Add("agent start"));

        executor = new MaintenanceExecutor(agent.Object, Mock.Logger<MaintenanceExecutor>());
        var recovery = new PodmanRecovery(health.Object, machine.Object, scripts.Object, options, time, Mock.Logger<PodmanRecovery>());
        workflow = new DiskMaintenanceWorkflow(disk.Object, scripts.Object, recovery, executor, options, time, logger);
    }

    public void Dispose() => executor.Dispose();

    [Test]
    public async Task RunAsync_EnoughSpace_DoesNothing()
    {
        disk.GetDiskSpace().Returns(TestData.Disk(freeGb: 50));

        await Assert.That(await workflow.RunAsync(MaintenanceTrigger.Periodic, TestData.Ct)).IsEqualTo(DiskMaintenanceOutcome.NotRequired);

        await Assert.That(calls).IsEmpty();
    }

    [Test]
    public async Task RunAsync_NightlyWithEnoughSpace_LogsInformation()
    {
        disk.GetDiskSpace().Returns(TestData.Disk(freeGb: 300));

        await Assert.That(await workflow.RunAsync(MaintenanceTrigger.Nightly, TestData.Ct)).IsEqualTo(DiskMaintenanceOutcome.NotRequired);

        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("300.0 GB frei").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_LowSpace_CleanupFreesEnough_NoPodmanReset()
    {
        disk.GetDiskSpace().ReturnsSequentially(TestData.Disk(freeGb: 40), TestData.Disk(freeGb: 250));

        await Assert.That(await workflow.RunAsync(MaintenanceTrigger.Periodic, TestData.Ct)).IsEqualTo(DiskMaintenanceOutcome.CleanupOnly);

        await Assert.That(string.Join(" > ", calls)).IsEqualTo("agent stop > cleanup > agent start");
        health.CheckAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_CleanupLeavesLessThan200Gb_ResetsPodmanAndEnsuresItRuns()
    {
        disk.GetDiskSpace().ReturnsSequentially(TestData.Disk(freeGb: 40), TestData.Disk(freeGb: 120), TestData.Disk(freeGb: 210));
        health.CheckAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(TestData.MachineStopped(), TestData.Healthy());

        await Assert.That(await workflow.RunAsync(MaintenanceTrigger.Periodic, TestData.Ct)).IsEqualTo(DiskMaintenanceOutcome.CleanupAndPodmanReset);

        await Assert.That(string.Join(" > ", calls)).IsEqualTo("agent stop > cleanup > reset > machine start > agent start");
        logger.VerifyLog().ContainingMessage("frei vorher 40.0 GB, nach Bereinigung 120.0 GB, final 210.0 GB").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_StillBelow50GbAfterMaintenance_LogsError()
    {
        disk.GetDiskSpace().ReturnsSequentially(TestData.Disk(freeGb: 40), TestData.Disk(freeGb: 42), TestData.Disk(freeGb: 45));

        await workflow.RunAsync(MaintenanceTrigger.Periodic, TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("45.0 GB frei").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_CleanupScriptThrows_ContinuesWithResetAndRestartsAgent()
    {
        disk.GetDiskSpace().Returns(TestData.Disk(freeGb: 40));
        scripts.RunJenkinsWorkspaceCleanupAsync(Arg.Any<CancellationToken>()).Throws(new FileNotFoundException("weg"));

        await Assert.That(await workflow.RunAsync(MaintenanceTrigger.Periodic, TestData.Ct)).IsEqualTo(DiskMaintenanceOutcome.CleanupAndPodmanReset);

        logger.VerifyLog().AtLevel(LogLevel.Error).WithException<FileNotFoundException>().WasCalled(Times.Once);
        agent.StartAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_PeriodicWithinCooldown_IsSkipped_NightlyIsNot()
    {
        disk.GetDiskSpace().Returns(TestData.Disk(freeGb: 40));

        await Assert.That(await workflow.RunAsync(MaintenanceTrigger.Periodic, TestData.Ct)).IsEqualTo(DiskMaintenanceOutcome.CleanupAndPodmanReset);

        time.Advance(TimeSpan.FromMinutes(30));
        await Assert.That(await workflow.RunAsync(MaintenanceTrigger.Periodic, TestData.Ct)).IsEqualTo(DiskMaintenanceOutcome.SkippedCooldown);
        await Assert.That(await workflow.RunAsync(MaintenanceTrigger.Nightly, TestData.Ct)).IsEqualTo(DiskMaintenanceOutcome.CleanupAndPodmanReset);

        time.Advance(TimeSpan.FromHours(2));
        await Assert.That(await workflow.RunAsync(MaintenanceTrigger.Periodic, TestData.Ct)).IsEqualTo(DiskMaintenanceOutcome.CleanupAndPodmanReset);

        scripts.RunJenkinsWorkspaceCleanupAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(3));
    }
}
