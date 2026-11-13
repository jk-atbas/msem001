using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Processes;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Podman;

public sealed class PodmanCliTests
{
    private readonly Mock<IProcessRunner> runner = Mock.Of<IProcessRunner>();
    private readonly MockLogger<PodmanCli> logger = Mock.Logger<PodmanCli>();
    private readonly PodmanCli cli;

    public PodmanCliTests()
    {
        cli = new PodmanCli(runner.Object, TestData.CreateOptions(), logger);
    }

    [Test]
    [Arguments("running", (int)PodmanMachineState.Running)]
    [Arguments("Running\r\n", (int)PodmanMachineState.Running)]
    [Arguments("stopped", (int)PodmanMachineState.Stopped)]
    [Arguments("starting", (int)PodmanMachineState.Starting)]
    [Arguments("whatever", (int)PodmanMachineState.Unknown)]
    [Arguments("", (int)PodmanMachineState.Unknown)]
    public async Task ParseState_MapsInspectOutput(string output, int expected)
    {
        await Assert.That(PodmanCli.ParseState(output)).IsEqualTo((PodmanMachineState)expected);
    }

    [Test]
    public async Task GetStateAsync_RunsInspectForConfiguredMachine()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Success("running"));

        await Assert.That(await cli.GetStateAsync(TestData.Ct)).IsEqualTo(PodmanMachineState.Running);

        runner.RunAsync(
            Arg.Is<ProcessRunRequest>(r => r.FileName == "podman"
                && string.Join(' ', r.Arguments) == "machine inspect --format {{.State}} podman-machine-default"
                && r.Timeout == TimeSpan.FromMinutes(1)),
            Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task GetStateAsync_MachineDoesNotExist_ReturnsMissing()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Failure(125, "Error: podman-machine-default: VM does not exist"));

        await Assert.That(await cli.GetStateAsync(TestData.Ct)).IsEqualTo(PodmanMachineState.Missing);
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("does not exist").WasCalled(Times.Once);
    }

    [Test]
    public async Task GetStateAsync_OtherFailure_ReturnsUnknown()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Failure(1, "boom"));

        await Assert.That(await cli.GetStateAsync(TestData.Ct)).IsEqualTo(PodmanMachineState.Unknown);
    }

    [Test]
    public async Task StartAsync_Success_ReturnsTrue()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Success());

        await Assert.That(await cli.StartAsync(TestData.Ct)).IsTrue();

        runner.RunAsync(
            Arg.Is<ProcessRunRequest>(r => string.Join(' ', r.Arguments) == "machine start podman-machine-default" && r.Timeout == TimeSpan.FromMinutes(5)),
            Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task StartAsync_AlreadyRunning_ReturnsTrue()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Failure(125, "Error: unable to start: machine podman-machine-default: VM already running or starting"));

        await Assert.That(await cli.StartAsync(TestData.Ct)).IsTrue();
    }

    [Test]
    public async Task StartAsync_Failure_ReturnsFalseAndWarns()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Failure(125, "gvproxy failed"));

        await Assert.That(await cli.StartAsync(TestData.Ct)).IsFalse();
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("gvproxy failed").WasCalled(Times.Once);
    }
}
