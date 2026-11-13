using Microsoft.Extensions.Time.Testing;
using Msem001.Watchdog.Jenkins;
using Msem001.Watchdog.Maintenance;
using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Scripts;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Maintenance;

public sealed class BuildAgentHealthMonitorTests : IDisposable
{
    private readonly Mock<IPodmanHealthChecker> health = Mock.Of<IPodmanHealthChecker>();
    private readonly Mock<IPodmanMachine> machine = Mock.Of<IPodmanMachine>();
    private readonly Mock<IMaintenanceScripts> scripts = Mock.Of<IMaintenanceScripts>();
    private readonly Mock<IJenkinsAgent> agent = Mock.Of<IJenkinsAgent>();
    private readonly Mock<IJenkinsAgentDrain> drain = Mock.Of<IJenkinsAgentDrain>();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));
    private readonly List<string> calls = [];
    private readonly MaintenanceExecutor executor;
    private readonly BuildAgentHealthMonitor monitor;

    public BuildAgentHealthMonitorTests()
    {
        var options = TestData.CreateOptions();
        scripts.RunPodmanResetAsync(Arg.Any<CancellationToken>()).Returns(TestData.Success()).Callback(() => calls.Add("reset"));
        machine.StartAsync(Arg.Any<CancellationToken>()).Returns(true).Callback(() => calls.Add("machine start"));
        agent.StopAsync(Arg.Any<CancellationToken>()).Callback(() => calls.Add("agent stop"));
        agent.StartAsync(Arg.Any<CancellationToken>()).Callback(() => calls.Add("agent start"));

        executor = new MaintenanceExecutor(agent.Object, drain.Object, Mock.Logger<MaintenanceExecutor>());
        var recovery = new PodmanRecovery(health.Object, machine.Object, scripts.Object, options, time, Mock.Logger<PodmanRecovery>());
        monitor = new BuildAgentHealthMonitor(health.Object, recovery, executor, options, time, Mock.Logger<BuildAgentHealthMonitor>());
    }

    public void Dispose() => executor.Dispose();

    [Test]
    public async Task CheckAsync_Healthy_EnsuresAgentIsRunning()
    {
        health.CheckAsync(Arg.Any<CancellationToken>()).Returns(TestData.Healthy());
        agent.IsRunning().Returns(false);

        await Assert.That(await monitor.CheckAsync(TestData.Ct)).IsEqualTo(HealthCheckOutcome.Healthy);

        await Assert.That(string.Join(" > ", calls)).IsEqualTo("agent start");
    }

    [Test]
    public async Task CheckAsync_ProxyBroken_ResetsOnlyAfterThreshold()
    {
        // The third check happens inside the repair, right before the reset.
        health.CheckAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(TestData.ProxyBroken(), TestData.ProxyBroken(), TestData.ProxyBroken(), TestData.Healthy());

        await Assert.That(await monitor.CheckAsync(TestData.Ct)).IsEqualTo(HealthCheckOutcome.Degraded);
        await Assert.That(calls).IsEmpty();

        await Assert.That(await monitor.CheckAsync(TestData.Ct)).IsEqualTo(HealthCheckOutcome.Repaired);
        await Assert.That(string.Join(" > ", calls)).IsEqualTo("agent stop > reset > agent start");
    }

    [Test]
    public async Task CheckAsync_MachineStoppedAfterReboot_IsRepairedImmediatelyByStart()
    {
        health.CheckAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(TestData.MachineStopped(), TestData.MachineStopped(), TestData.Healthy());

        await Assert.That(await monitor.CheckAsync(TestData.Ct)).IsEqualTo(HealthCheckOutcome.Repaired);

        await Assert.That(string.Join(" > ", calls)).IsEqualTo("agent stop > machine start > agent start");
    }

    [Test]
    public async Task CheckAsync_RepairFails_IsRetriedOnlyAfterRetryInterval()
    {
        health.CheckAsync(Arg.Any<CancellationToken>()).Returns(TestData.MachineStopped());

        await Assert.That(await monitor.CheckAsync(TestData.Ct)).IsEqualTo(HealthCheckOutcome.RepairFailed);

        time.Advance(TimeSpan.FromMinutes(10));
        await Assert.That(await monitor.CheckAsync(TestData.Ct)).IsEqualTo(HealthCheckOutcome.RepairDeferred);

        time.Advance(TimeSpan.FromMinutes(25));
        await Assert.That(await monitor.CheckAsync(TestData.Ct)).IsEqualTo(HealthCheckOutcome.RepairFailed);

        scripts.RunPodmanResetAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(2));
    }

    [Test]
    public async Task CheckAsync_RecoveryAfterFailures_ResetsCounter()
    {
        health.CheckAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(TestData.ProxyBroken(), TestData.Healthy(), TestData.ProxyBroken());

        await Assert.That(await monitor.CheckAsync(TestData.Ct)).IsEqualTo(HealthCheckOutcome.Degraded);
        await Assert.That(await monitor.CheckAsync(TestData.Ct)).IsEqualTo(HealthCheckOutcome.Healthy);
        await Assert.That(await monitor.CheckAsync(TestData.Ct)).IsEqualTo(HealthCheckOutcome.Degraded);
    }

    [Test]
    public async Task CheckAsync_MaintenanceRunning_IsSkipped()
    {
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = executor.RunWithAgentStoppedAsync("Test", _ => release.Task, TestData.Ct);

        await Assert.That(await monitor.CheckAsync(TestData.Ct)).IsEqualTo(HealthCheckOutcome.SkippedBusy);
        health.CheckAsync(Arg.Any<CancellationToken>()).WasNeverCalled();

        release.SetResult(0);
        await running;
    }
}
