using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Processes;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Podman;

public sealed class PodmanHealthCheckerTests
{
    private readonly Mock<IPodmanMachine> machine = Mock.Of<IPodmanMachine>();
    private readonly Mock<IDockerApiProbe> probe = Mock.Of<IDockerApiProbe>();
    private readonly Mock<ISystemProcesses> processes = Mock.Of<ISystemProcesses>();
    private readonly PodmanHealthChecker checker;

    public PodmanHealthCheckerTests()
    {
        processes.CountByName("win-sshproxy").Returns(1);
        checker = new PodmanHealthChecker(machine.Object, probe.Object, processes.Object, Mock.Logger<PodmanHealthChecker>());
    }

    [Test]
    public async Task CheckAsync_RunningAndPingOk_IsHealthy()
    {
        machine.GetStateAsync(Arg.Any<CancellationToken>()).Returns(PodmanMachineState.Running);
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(new DockerApiProbeResult(true, "ok"));

        var health = await checker.CheckAsync(TestData.Ct);

        await Assert.That(health.IsHealthy).IsTrue();
        await Assert.That(health.SshProxyProcessCount).IsEqualTo(1);
    }

    [Test]
    public async Task CheckAsync_RunningButProxyDead_IsUnhealthy()
    {
        machine.GetStateAsync(Arg.Any<CancellationToken>()).Returns(PodmanMachineState.Running);
        probe.ProbeAsync(Arg.Any<CancellationToken>()).Returns(new DockerApiProbeResult(false, "pipe not found"));

        var health = await checker.CheckAsync(TestData.Ct);

        await Assert.That(health.IsHealthy).IsFalse();
        await Assert.That(health.ToString()).Contains("pipe not found").And.Contains("nicht erreichbar");
    }

    [Test]
    public async Task CheckAsync_MachineStopped_DoesNotProbe()
    {
        machine.GetStateAsync(Arg.Any<CancellationToken>()).Returns(PodmanMachineState.Stopped);

        var health = await checker.CheckAsync(TestData.Ct);

        await Assert.That(health.IsHealthy).IsFalse();
        await Assert.That(health.MachineState).IsEqualTo(PodmanMachineState.Stopped);
        probe.ProbeAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
    }
}
