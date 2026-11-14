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
        Containers(Running("frisch", TimeSpan.FromMinutes(5)), Running("grenzwertig", TimeSpan.FromHours(1)), Running("liegengeblieben", TimeSpan.FromHours(3)));

        await Assert.That(await CreateSut().RunAsync(TestData.Ct)).IsEqualTo(1);

        await Assert.That(removed).IsEquivalentTo(["liegengeblieben"]);
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("Container liegengeblieben (image) lief seit 03:00:00 (erlaubt 01:00:00) und wurde entfernt").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_UsesConfiguredMaxRunTime()
    {
        Containers(Running("kurz", TimeSpan.FromMinutes(20)));

        await Assert.That(await CreateSut(o => o.Containers.MaxRunTime = TimeSpan.FromMinutes(15)).RunAsync(TestData.Ct)).IsEqualTo(1);
    }

    [Test]
    public async Task RunAsync_RemovesLeftOverCreatedAndExitedContainers()
    {
        Containers(
            Stopped("nie-gestartet", "created", createdAgo: TimeSpan.FromHours(2), exitedAgo: null),
            Stopped("beendet", "exited", createdAgo: TimeSpan.FromHours(5), exitedAgo: TimeSpan.FromHours(2)),
            Stopped("gerade-beendet", "exited", createdAgo: TimeSpan.FromHours(5), exitedAgo: TimeSpan.FromMinutes(10)),
            Stopped("gerade-angelegt", "created", createdAgo: TimeSpan.FromMinutes(1), exitedAgo: null));

        await Assert.That(await CreateSut().RunAsync(TestData.Ct)).IsEqualTo(2);

        await Assert.That(removed).IsEquivalentTo(["nie-gestartet", "beendet"]);
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("Liegengebliebener Container beendet (image, Zustand exited seit 02:00:00, erlaubt 01:00:00) wurde entfernt").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_UsesConfiguredMaxStoppedTime()
    {
        Containers(Stopped("beendet", "exited", TimeSpan.FromHours(1), TimeSpan.FromMinutes(20)), Running("läuft", TimeSpan.FromMinutes(20)));

        await CreateSut(o => o.Containers.MaxStoppedTime = TimeSpan.FromMinutes(15)).RunAsync(TestData.Ct);

        await Assert.That(removed).IsEquivalentTo(["beendet"]);
    }

    [Test]
    public async Task RunAsync_RemoveStoppedDisabled_KeepsNotRunningContainers()
    {
        Containers(Stopped("beendet", "exited", TimeSpan.FromDays(1), TimeSpan.FromDays(1)), Running("alt", TimeSpan.FromDays(1)));

        await CreateSut(o => o.Containers.RemoveStopped = false).RunAsync(TestData.Ct);

        await Assert.That(removed).IsEquivalentTo(["alt"]);
    }

    [Test]
    public async Task RunAsync_ContainerBeingRemoved_IsLeftAlone()
    {
        Containers(Stopped("verschwindet", "removing", TimeSpan.FromDays(1), null));

        await Assert.That(await CreateSut().RunAsync(TestData.Ct)).IsEqualTo(0);
        containers.RemoveAsync(Arg.Any<PodmanContainer>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task RunAsync_ExcludedNamePrefix_IsKept()
    {
        Containers(Running("Keep-Registry", TimeSpan.FromDays(2)), Stopped("keep-cache", "exited", TimeSpan.FromDays(2), TimeSpan.FromDays(2)), Running("alt", TimeSpan.FromDays(2)));

        await CreateSut(o => o.Containers.ExcludedNamePrefixes = [" ", "keep-"]).RunAsync(TestData.Ct);

        await Assert.That(removed).IsEquivalentTo(["alt"]);
    }

    [Test]
    public async Task RunAsync_RemovalFails_IsNotCounted()
    {
        Containers(Running("hartnäckig", TimeSpan.FromHours(2)));
        containers.RemoveAsync(Arg.Any<PodmanContainer>(), Arg.Any<CancellationToken>()).Returns(false);

        await Assert.That(await CreateSut().RunAsync(TestData.Ct)).IsEqualTo(0);
        logger.VerifyLog().ContainingMessage("entfernt").AtLevel(LogLevel.Warning).WasNeverCalled();
    }

    [Test]
    public async Task RunAsync_ListFails_RemovesNothing()
    {
        containers.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PodmanContainer>?)null);

        await Assert.That(await CreateSut().RunAsync(TestData.Ct)).IsEqualTo(0);
        containers.RemoveAsync(Arg.Any<PodmanContainer>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task RunAsync_WhileMaintenanceRuns_IsSkipped()
    {
        Containers(Running("alt", TimeSpan.FromHours(2)));
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var maintenance = executor.RunWithAgentStoppedAsync("Podman-Reparatur", _ => release.Task, TestData.Ct);
        await Wait.UntilAsync(() => executor.IsBusy);

        await Assert.That(await CreateSut().RunAsync(TestData.Ct)).IsEqualTo(0);

        containers.ListAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        release.SetResult(0);
        await maintenance;
    }

    private static PodmanContainer Running(string name, TimeSpan startedAgo) =>
        new(name + "-id", name, "image", "running", Now - startedAgo - TimeSpan.FromSeconds(5), Now - startedAgo, null);

    private static PodmanContainer Stopped(string name, string state, TimeSpan createdAgo, TimeSpan? exitedAgo) =>
        new(name + "-id", name, "image", state, Now - createdAgo, exitedAgo is null ? null : Now - createdAgo, Now - exitedAgo);

    private void Containers(params PodmanContainer[] all) =>
        containers.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<PodmanContainer>?)all);

    private ContainerCleanup CreateSut(Action<WatchdogOptions>? configure = null) =>
        new(containers.Object, executor, TestData.CreateOptions(configure), time, logger);
}
