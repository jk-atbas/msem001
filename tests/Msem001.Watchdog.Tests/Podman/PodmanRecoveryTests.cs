using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Scripts;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Podman;

public sealed class PodmanRecoveryTests
{
    private readonly Mock<IPodmanHealthChecker> health = Mock.Of<IPodmanHealthChecker>();
    private readonly Mock<IPodmanMachine> machine = Mock.Of<IPodmanMachine>();
    private readonly Mock<IMaintenanceScripts> scripts = Mock.Of<IMaintenanceScripts>();
    private readonly MockLogger<PodmanRecovery> logger = Mock.Logger<PodmanRecovery>();
    private readonly PodmanRecovery recovery;

    public PodmanRecoveryTests()
    {
        machine.StartAsync(Arg.Any<CancellationToken>()).Returns(true);
        scripts.RunPodmanResetAsync(Arg.Any<CancellationToken>()).Returns(TestData.Success());
        recovery = new PodmanRecovery(health.Object, machine.Object, scripts.Object, TestData.CreateOptions(), TimeProvider.System, logger);
    }

    [Test]
    public async Task EnsureHealthyAsync_Healthy_DoesNothing()
    {
        health.CheckAsync(Arg.Any<CancellationToken>()).Returns(TestData.Healthy());

        var result = await recovery.EnsureHealthyAsync(allowReset: true, TestData.Ct);

        await Assert.That(result.IsHealthy).IsTrue();
        machine.StartAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        scripts.RunPodmanResetAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task EnsureHealthyAsync_MachineStopped_StartFixesIt_NoReset()
    {
        health.CheckAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(TestData.MachineStopped(), TestData.Healthy());

        var result = await recovery.EnsureHealthyAsync(allowReset: true, TestData.Ct);

        await Assert.That(result.IsHealthy).IsTrue();
        machine.StartAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
        scripts.RunPodmanResetAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("durch Start wiederhergestellt").WasCalled(Times.Once);
    }

    [Test]
    public async Task EnsureHealthyAsync_ProxyStillBrokenAfterStart_ResetsMachine()
    {
        // Typical after a reboot: machine stopped, after the start win-sshproxy dies, only a reset helps.
        health.CheckAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(TestData.MachineStopped(), TestData.ProxyBroken(), TestData.Healthy());

        var result = await recovery.EnsureHealthyAsync(allowReset: true, TestData.Ct);

        await Assert.That(result.IsHealthy).IsTrue();
        Mock.VerifyInOrder(() =>
        {
            machine.StartAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
            scripts.RunPodmanResetAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
        });
    }

    [Test]
    public async Task EnsureHealthyAsync_RunningButProxyBroken_ResetsWithoutStart()
    {
        health.CheckAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(TestData.ProxyBroken(), TestData.Healthy());

        var result = await recovery.EnsureHealthyAsync(allowReset: true, TestData.Ct);

        await Assert.That(result.IsHealthy).IsTrue();
        machine.StartAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        scripts.RunPodmanResetAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task EnsureHealthyAsync_ResetNotAllowed_OnlyStartsAndWarns()
    {
        health.CheckAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(TestData.MachineStopped(), TestData.ProxyBroken());

        var result = await recovery.EnsureHealthyAsync(allowReset: false, TestData.Ct);

        await Assert.That(result.IsHealthy).IsFalse();
        scripts.RunPodmanResetAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        await Assert.That(logger.Latest().LogLevel).IsEqualTo(LogLevel.Warning);
    }

    [Test]
    public async Task ResetAsync_MachineNotRunningAfterScript_StartsIt()
    {
        health.CheckAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(TestData.MachineStopped(), TestData.Healthy());

        var result = await recovery.ResetAsync("Test", TestData.Ct);

        await Assert.That(result.IsHealthy).IsTrue();
        machine.StartAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("nach Reset gesund").WasCalled(Times.Once);
    }

    [Test]
    public async Task ResetAsync_ProxyNeedsTime_ChecksAgain()
    {
        health.CheckAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(TestData.ProxyBroken(), TestData.Healthy());

        var result = await recovery.ResetAsync("Test", TestData.Ct);

        await Assert.That(result.IsHealthy).IsTrue();
        health.CheckAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(2));
        machine.StartAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ResetAsync_StillBroken_LogsCritical()
    {
        health.CheckAsync(Arg.Any<CancellationToken>()).Returns(TestData.ProxyBroken());

        var result = await recovery.ResetAsync("Test", TestData.Ct);

        await Assert.That(result.IsHealthy).IsFalse();
        logger.VerifyLog().AtLevel(LogLevel.Critical).ContainingMessage("manuelles Eingreifen").WasCalled(Times.Once);
    }

    [Test]
    public async Task ResetAsync_ScriptThrows_LogsErrorAndStillVerifies()
    {
        scripts.RunPodmanResetAsync(Arg.Any<CancellationToken>()).Throws(new FileNotFoundException("missing"));
        health.CheckAsync(Arg.Any<CancellationToken>()).Returns(TestData.Healthy());

        var result = await recovery.ResetAsync("Test", TestData.Ct);

        await Assert.That(result.IsHealthy).IsTrue();
        logger.VerifyLog().AtLevel(LogLevel.Error).WithException<FileNotFoundException>().WasCalled(Times.Once);
    }
}
