using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Processes;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Podman;

public sealed class PodmanContainerCliTests
{
    // Shortened output of podman 6.1 (podman ps --format json)
    private const string PsOutput = """
        [
          {
            "Id": "ed1ab8a11107",
            "Image": "docker.io/library/postgres:16",
            "IsInfra": false,
            "Names": [ "testcontainers-postgres" ],
            "StartedAt": 1794638825,
            "State": "running",
            "Created": 1794638800
          },
          {
            "Id": "infra000",
            "Image": "localhost/podman-pause",
            "IsInfra": true,
            "Names": [ "pod-infra" ],
            "StartedAt": 1794638825,
            "Created": 1794638825
          },
          {
            "Id": "noname01",
            "StartedAt": 0,
            "Created": 1794638700
          }
        ]
        """;

    private static readonly PodmanContainer Container = new("ed1ab8a11107", "testcontainers-postgres", "postgres", DateTimeOffset.UnixEpoch);

    private readonly Mock<IProcessRunner> runner = Mock.Of<IProcessRunner>();
    private readonly MockLogger<PodmanContainerCli> logger = Mock.Logger<PodmanContainerCli>();
    private readonly PodmanContainerCli cli;

    public PodmanContainerCliTests()
    {
        cli = new PodmanContainerCli(runner.Object, TestData.CreateOptions(), logger);
    }

    [Test]
    public async Task ParseContainers_ReadsNameImageAndStart_SkipsInfraContainers()
    {
        var containers = PodmanContainerCli.ParseContainers(PsOutput);

        await Assert.That(containers).IsEquivalentTo(new[]
        {
            new PodmanContainer("ed1ab8a11107", "testcontainers-postgres", "docker.io/library/postgres:16", DateTimeOffset.FromUnixTimeSeconds(1794638825)),
            new PodmanContainer("noname01", "noname01", string.Empty, DateTimeOffset.FromUnixTimeSeconds(1794638700)),
        });
    }

    [Test]
    [Arguments("")]
    [Arguments("  ")]
    [Arguments("[]")]
    public async Task ParseContainers_NoContainers_ReturnsEmpty(string output)
    {
        await Assert.That(PodmanContainerCli.ParseContainers(output)).IsEmpty();
    }

    [Test]
    public async Task ListRunningAsync_RunsPodmanPsAsJson()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Success(PsOutput.Split('\n')));

        var containers = await cli.ListRunningAsync(TestData.Ct);

        await Assert.That(containers!.Count).IsEqualTo(2);
        runner.RunAsync(
            Arg.Is<ProcessRunRequest>(r => r.FileName == "podman" && string.Join(' ', r.Arguments) == "ps --format json" && r.Timeout == TimeSpan.FromMinutes(1)),
            Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task ListRunningAsync_PodmanFails_ReturnsNullAndWarns()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Failure(125, "Cannot connect to Podman"));

        await Assert.That(await cli.ListRunningAsync(TestData.Ct)).IsNull();

        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("Cannot connect to Podman").WasCalled(Times.Once);
    }

    [Test]
    [Arguments("{\"Id\": \"x\"}")]
    [Arguments("[{\"Names\": [\"ohne-id\"], \"Created\": 1}]")]
    [Arguments("[{\"Id\": \"x\", \"StartedAt\": 0}]")]
    [Arguments("[{\"Id\": 5, \"Created\": 1}]")]
    [Arguments("Error: kein JSON")]
    public async Task ListRunningAsync_UnreadableOutput_ReturnsNullAndWarns(string output)
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Success(output));

        await Assert.That(await cli.ListRunningAsync(TestData.Ct)).IsNull();

        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("nicht lesbar").WasCalled(Times.Once);
    }

    [Test]
    public async Task RemoveAsync_ForcesRemovalWithStopTimeoutAndVolumes()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Success());

        await Assert.That(await cli.RemoveAsync(Container, TestData.Ct)).IsTrue();

        runner.RunAsync(
            Arg.Is<ProcessRunRequest>(r => string.Join(' ', r.Arguments) == "rm --force --volumes --time 10 ed1ab8a11107"),
            Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task RemoveAsync_AlreadyGone_IsSuccess()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>())
            .Returns(TestData.Failure(1, "Error: no container with name or ID \"ed1ab8a11107\" found: no such container"));

        await Assert.That(await cli.RemoveAsync(Container, TestData.Ct)).IsTrue();
        logger.VerifyNoLog(LogLevel.Warning);
    }

    [Test]
    public async Task RemoveAsync_Fails_ReturnsFalseAndWarns()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Failure(125, "device busy"));

        await Assert.That(await cli.RemoveAsync(Container, TestData.Ct)).IsFalse();

        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("testcontainers-postgres konnte nicht entfernt werden").WasCalled(Times.Once);
    }
}
