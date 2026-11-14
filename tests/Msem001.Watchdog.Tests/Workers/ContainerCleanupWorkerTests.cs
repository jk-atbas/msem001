using Microsoft.Extensions.Time.Testing;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Jenkins;
using Msem001.Watchdog.Maintenance;
using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Tests.TestSupport;
using Msem001.Watchdog.Workers;

namespace Msem001.Watchdog.Tests.Workers;

public sealed class ContainerCleanupWorkerTests : IDisposable
{
    private readonly Mock<IPodmanContainers> containers = Mock.Of<IPodmanContainers>();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero));
    private readonly MockLogger<ContainerCleanupWorker> logger = Mock.Logger<ContainerCleanupWorker>();
    private readonly MaintenanceExecutor executor = new(Mock.Of<IJenkinsAgent>().Object, Mock.Of<IJenkinsAgentDrain>().Object, Mock.Logger<MaintenanceExecutor>());
    private int lists;

    public ContainerCleanupWorkerTests()
    {
        containers.ListAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PodmanContainer>?)Array.Empty<PodmanContainer>())
            .Callback(() => Interlocked.Increment(ref lists));
    }

    public void Dispose() => executor.Dispose();

    [Test]
    public async Task ExecuteAsync_ChecksAfterStartupDelayAndThenCyclically()
    {
        using var worker = CreateSut();

        await worker.StartAsync(TestData.Ct);
        await Wait.UntilAsync(() => Volatile.Read(ref lists) >= 3, time, TimeSpan.FromMinutes(1));
        await worker.StopAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("laufend länger als 01:00:00 oder liegengeblieben").WasCalled(Times.Once);
    }

    [Test]
    public async Task ExecuteAsync_Disabled_DoesNotCheck()
    {
        using var worker = CreateSut(o => o.Containers.Enabled = false);

        await worker.StartAsync(TestData.Ct);
        time.Advance(TimeSpan.FromHours(1));
        await worker.StopAsync(TestData.Ct);

        containers.ListAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("deaktiviert").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunCycleAsync_CleanupThrows_LogsError()
    {
        containers.ListAsync(Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("kaputt"));
        using var worker = CreateSut();

        await worker.RunCycleAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Error).WithException<InvalidOperationException>().WasCalled(Times.Once);
    }

    private ContainerCleanupWorker CreateSut(Action<WatchdogOptions>? configure = null)
    {
        var options = TestData.CreateOptions(configure);
        var cleanup = new ContainerCleanup(containers.Object, executor, options, time, Mock.Logger<ContainerCleanup>());
        return new ContainerCleanupWorker(cleanup, options, time, logger);
    }
}
