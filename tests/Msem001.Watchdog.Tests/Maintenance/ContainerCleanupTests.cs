using Microsoft.Extensions.Time.Testing;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Jenkins;
using Msem001.Watchdog.Maintenance;
using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Maintenance;

public sealed class ContainerCleanupTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

    private readonly Mock<IPodmanContainers> containers = Mock.Of<IPodmanContainers>();
    private readonly Mock<IJenkinsAgentDrain> drain = Mock.Of<IJenkinsAgentDrain>();
    private readonly FakeTimeProvider time = new(Now);
    private readonly MockLogger<ContainerCleanup> logger = Mock.Logger<ContainerCleanup>();
    private readonly List<string> removed = [];
    private readonly MaintenanceExecutor executor;

    public ContainerCleanupTests()
    {
        containers.RemoveAsync(Arg.Any<PodmanContainer>(), Arg.Any<CancellationToken>()).Returns(true).Callback((c, _) => removed.Add(c.Name));
        executor = new MaintenanceExecutor(Mock.Of<IJenkinsAgent>().Object, drain.Object, Mock.Logger<MaintenanceExecutor>());
    }

    public void Dispose() => executor.Dispose();

    [Test]
    public async Task RunAsync_RemovesOnlyContainersRunningLongerThanMaxRunTime()
    {
        Running(Started("frisch", TimeSpan.FromMinutes(5)), Started("grenzwertig", TimeSpan.FromHours(1)), Started("liegengeblieben", TimeSpan.FromHours(3)));

        await Assert.That(await CreateSut().RunAsync(TestData.Ct)).IsEqualTo(1);

        await Assert.That(removed).IsEquivalentTo(["liegengeblieben"]);
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("Container liegengeblieben (image) lief seit 03:00:00 (erlaubt 01:00:00) und wurde entfernt").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_UsesConfiguredMaxRunTime()
    {
        Running(Started("kurz", TimeSpan.FromMinutes(20)));

        await Assert.That(await CreateSut(o => o.Containers.MaxRunTime = TimeSpan.FromMinutes(15)).RunAsync(TestData.Ct)).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_ExcludedNamePrefix_IsKept()
    {
        Running(Started("Keep-Registry", TimeSpan.FromDays(2)), Started("alt", TimeSpan.FromDays(2)));

        await CreateSut(o => o.Containers.ExcludedNamePrefixes = [" ", "keep-"]).RunAsync(TestData.Ct);

        await Assert.That(removed).IsEquivalentTo(["alt"]);
    }

    [Test]
    public async Task RunAsync_RemovalFails_IsNotCounted()
    {
        Running(Started("hartnäckig", TimeSpan.FromHours(2)));
        containers.RemoveAsync(Arg.Any<PodmanContainer>(), Arg.Any<CancellationToken>()).Returns(false);

        await Assert.That(await CreateSut().RunAsync(TestData.Ct)).IsEqualTo(0);
        logger.VerifyLog().ContainingMessage("wurde entfernt").WasNeverCalled();
    }

    [Test]
    public async Task RunAsync_ListFails_RemovesNothing()
    {
        containers.ListRunningAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PodmanContainer>?)null);

        await Assert.That(await CreateSut().RunAsync(TestData.Ct)).IsEqualTo(0);
        containers.RemoveAsync(Arg.Any<PodmanContainer>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task RunAsync_WhileMaintenanceRuns_IsSkipped()
    {
        Running(Started("alt", TimeSpan.FromHours(2)));
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var maintenance = executor.RunWithAgentStoppedAsync("Podman-Reparatur", _ => release.Task, TestData.Ct);
        await Wait.UntilAsync(() => executor.IsBusy);

        await Assert.That(await CreateSut().RunAsync(TestData.Ct)).IsEqualTo(0);

        containers.ListRunningAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        release.SetResult(0);
        await maintenance;
    }

    private static PodmanContainer Started(string name, TimeSpan ago) => new(name + "-id", name, "image", Now - ago);

    private void Running(params PodmanContainer[] running) =>
        containers.ListRunningAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PodmanContainer>?)running);

    private ContainerCleanup CreateSut(Action<WatchdogOptions>? configure = null) =>
        new(containers.Object, executor, TestData.CreateOptions(configure), time, logger);
}
