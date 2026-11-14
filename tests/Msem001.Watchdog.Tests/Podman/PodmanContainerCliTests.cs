using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Processes;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Podman;

public sealed class PodmanContainerCliTests
{
    // Shortened output of podman 6.1 (podman ps --all --format json); unset times are Go zero time
    private const string PsOutput = """
        [
          {
            "Id": "ed1ab8a11107",
            "Image": "docker.io/library/postgres:16",
            "IsInfra": false,
            "Names": [ "testcontainers-postgres" ],
            "StartedAt": 1794638825,
            "ExitedAt": -62135596800,
            "State": "running",
            "Created": 1794638800
          },
          {
            "Id": "exited01",
            "Image": "alpine",
            "Names": [ "beendet" ],
            "State": "exited",
            "Created": 1794638000,
            "StartedAt": 1794638100,
            "ExitedAt": 1794638200
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
            "State": "created",
            "StartedAt": -62135596800,
            "Created": 1794638700
          }
        ]
        """;

    private static readonly PodmanContainer Container = new("ed1ab8a11107", "testcontainers-postgres", "postgres", "running", DateTimeOffset.UnixEpoch, null, null);

    private readonly Mock<IProcessRunner> runner = Mock.Of<IProcessRunner>();
    private readonly MockLogger<PodmanContainerCli> logger = Mock.Logger<PodmanContainerCli>();
    private readonly PodmanContainerCli cli;

    public PodmanContainerCliTests()
    {
        cli = new PodmanContainerCli(runner.Object, TestData.CreateOptions(), logger);
    }

    [Test]
    public async Task ParseContainers_ReadsStateAndTimes_SkipsInfraContainers()
    {
        var containers = PodmanContainerCli.ParseContainers(PsOutput);

        await Assert.That(containers).IsEquivalentTo(new[]
        {
            new PodmanContainer("ed1ab8a11107", "testcontainers-postgres", "docker.io/library/postgres:16", "running", At(1794638800), At(1794638825), null),
            new PodmanContainer("exited01", "beendet", "alpine", "exited", At(1794638000), At(1794638100), At(1794638200)),
            new PodmanContainer("noname01", "noname01", string.Empty, "created", At(1794638700), null, null),
        });
    }

    [Test]
    public async Task ParseContainers_Podman62OutputFromBuildAgent()
    {
        // podman 6.2.0 on MSEM001 (Testcontainers database, nested ExposedPorts/Ports/Labels)
        const string output = """
            [
              {
                "AutoRemove": false,
                "Command": [ "-c", "fsync=off", "--timezone=Europe/Berlin" ],
                "CreatedAt": "19 minutes ago",
                "CIDFile": "",
                "Exited": false,
                "ExitedAt": -62135596800,
                "ExitCode": 0,
                "ExposedPorts": {
                  "5432": [
              "tcp"
            ]
                },
                "Id": "17a17c58efd7f708505d82d6cbd3727025190723cc7e1c085e5ed55f8b7c3781",
                "Image": "10.24.9.62:2375/atbas_testdb:1.232.9713.28775",
                "IsInfra": false,
                "Labels": {
                  "org.testcontainers": "true",
                  "org.testcontainers.session-id": "7018ed0b-103e-4387-ae53-f1622bef5509"
                },
                "Mounts": [ "/var/lib/postgresql/data" ],
                "Names": [ "priceless_robinson" ],
                "Namespaces": {

                },
                "Networks": [ "podman" ],
                "Pid": 32461,
                "Ports": [ { "host_ip": "0.0.0.0", "container_port": 5432, "host_port": 35453, "range": 1, "protocol": "tcp" } ],
                "Restarts": 0,
                "Size": null,
                "StartedAt": 1794639215,
                "State": "running",
                "Status": "Up 19 minutes",
                "Created": 1794639208
              }
            ]
            """;

        var container = PodmanContainerCli.ParseContainers(output).Single();

        await Assert.That(container).IsEqualTo(new PodmanContainer(
            "17a17c58efd7f708505d82d6cbd3727025190723cc7e1c085e5ed55f8b7c3781",
            "priceless_robinson",
            "10.24.9.62:2375/atbas_testdb:1.232.9713.28775",
            "running",
            At(1794639208),
            At(1794639215),
            null));
        await Assert.That(container.IsRunning).IsTrue();
        await Assert.That(container.StateSince).IsEqualTo(At(1794639215));
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
    public async Task ListAsync_RunsPodmanPsForAllContainersAsJson()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Success(PsOutput.Split('\n')));

        var containers = await cli.ListAsync(TestData.Ct);

        await Assert.That(containers!.Count).IsEqualTo(3);
        runner.RunAsync(
            Arg.Is<ProcessRunRequest>(r => r.FileName == "podman" && string.Join(' ', r.Arguments) == "ps --all --format json" && r.Timeout == TimeSpan.FromMinutes(1)),
            Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task ListRunningAsync_PodmanFails_ReturnsNullAndWarns()
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Failure(125, "Cannot connect to Podman"));

        await Assert.That(await cli.ListAsync(TestData.Ct)).IsNull();

        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("Cannot connect to Podman").WasCalled(Times.Once);
    }

    [Test]
    [Arguments("{\"Id\": \"x\"}")]
    [Arguments("[{\"Names\": [\"ohne-id\"], \"Created\": 1}]")]
    [Arguments("[{\"Id\": \"x\", \"Created\": -62135596800}]")]
    [Arguments("[{\"Id\": 5, \"Created\": 1}]")]
    [Arguments("Error: kein JSON")]
    public async Task ListRunningAsync_UnreadableOutput_ReturnsNullAndWarns(string output)
    {
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Success(output));

        await Assert.That(await cli.ListAsync(TestData.Ct)).IsNull();

        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("nicht lesbar").WasCalled(Times.Once);
    }

    [Test]
    [Arguments("running", true)]
    [Arguments("Paused", true)]
    [Arguments("stopping", true)]
    [Arguments("created", false)]
    [Arguments("exited", false)]
    [Arguments("dead", false)]
    public async Task PodmanContainer_IsRunning_DependsOnState(string state, bool running)
    {
        await Assert.That(new PodmanContainer("id", "name", "image", state, At(1), null, null).IsRunning).IsEqualTo(running);
    }

    [Test]
    public async Task PodmanContainer_StateSince_UsesStartForRunningAndExitOrCreationOtherwise()
    {
        await Assert.That(new PodmanContainer("id", "n", "i", "running", At(1), At(2), At(3)).StateSince).IsEqualTo(At(2));
        await Assert.That(new PodmanContainer("id", "n", "i", "running", At(1), null, null).StateSince).IsEqualTo(At(1));
        await Assert.That(new PodmanContainer("id", "n", "i", "exited", At(1), At(2), At(3)).StateSince).IsEqualTo(At(3));
        await Assert.That(new PodmanContainer("id", "n", "i", "created", At(1), null, null).StateSince).IsEqualTo(At(1));
        await Assert.That(new PodmanContainer("id", "n", "i", "Removing", At(1), null, null).IsRemoving).IsTrue();
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

    private static DateTimeOffset At(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds);
}
