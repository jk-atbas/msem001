using Msem001.Watchdog.Jenkins;
using Msem001.Watchdog.Maintenance;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Maintenance;

public sealed class MaintenanceExecutorTests : IDisposable
{
    private readonly Mock<IJenkinsAgent> agent = Mock.Of<IJenkinsAgent>();
    private readonly MockLogger<MaintenanceExecutor> logger = Mock.Logger<MaintenanceExecutor>();
    private readonly List<string> calls = [];
    private readonly MaintenanceExecutor executor;

    public MaintenanceExecutorTests()
    {
        agent.StopAsync(Arg.Any<CancellationToken>()).Callback(() => calls.Add("stop"));
        agent.StartAsync(Arg.Any<CancellationToken>()).Callback(() => calls.Add("start"));
        executor = new MaintenanceExecutor(agent.Object, logger);
    }

    public void Dispose() => executor.Dispose();

    [Test]
    public async Task RunWithAgentStoppedAsync_StopsAgentBeforeAndStartsItAfterRoutine()
    {
        var result = await executor.RunWithAgentStoppedAsync(
            "Test",
            _ =>
            {
                calls.Add("routine");
                return Task.FromResult(42);
            },
            TestData.Ct);

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(string.Join(" > ", calls)).IsEqualTo("stop > routine > start");
    }

    [Test]
    public async Task RunWithAgentStoppedAsync_RoutineFails_AgentIsStartedAnyway()
    {
        await Assert.That(async () => { await executor.RunWithAgentStoppedAsync<int>("Test", _ => throw new InvalidOperationException("boom"), TestData.Ct); })
            .Throws<InvalidOperationException>();

        agent.StartAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
        await Assert.That(executor.IsBusy).IsFalse();
    }

    [Test]
    public async Task RunWithAgentStoppedAsync_StopFails_RoutineRunsAndErrorIsLogged()
    {
        agent.StopAsync(Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("kill failed"));

        var result = await executor.RunWithAgentStoppedAsync("Test", _ => Task.FromResult(1), TestData.Ct);

        await Assert.That(result).IsEqualTo(1);
        logger.VerifyLog().AtLevel(LogLevel.Error).WithException<InvalidOperationException>().ContainingMessage("nicht beendet").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunWithAgentStoppedAsync_StartFails_ErrorIsLogged()
    {
        agent.StartAsync(Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("start failed"));

        await executor.RunWithAgentStoppedAsync("Test", _ => Task.FromResult(1), TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("nicht wieder gestartet").WasCalled(Times.Once);
    }

    [Test]
    public async Task Routines_AreSerialized_AndAgentSupervisionIsSkippedWhileBusy()
    {
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = executor.RunWithAgentStoppedAsync("Erste", _ => release.Task, TestData.Ct);

        await Assert.That(executor.IsBusy).IsTrue();
        await Assert.That(await executor.TryEnsureAgentRunningAsync(TestData.Ct)).IsFalse();

        var secondStarted = false;
        Task<int> SecondRoutine(CancellationToken token)
        {
            secondStarted = true;
            return Task.FromResult(2);
        }

        var second = executor.RunWithAgentStoppedAsync("Zweite", SecondRoutine, TestData.Ct);
        await Task.Delay(100, TestData.Ct);
        await Assert.That(secondStarted).IsFalse();

        release.SetResult(1);
        await Assert.That(await first).IsEqualTo(1);
        await Assert.That(await second).IsEqualTo(2);
        await Assert.That(secondStarted).IsTrue();
    }

    [Test]
    public async Task TryEnsureAgentRunningAsync_AgentNotRunning_StartsIt()
    {
        agent.IsRunning().Returns(false);

        await Assert.That(await executor.TryEnsureAgentRunningAsync(TestData.Ct)).IsTrue();

        agent.StartAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("läuft nicht").WasCalled(Times.Once);
    }

    [Test]
    public async Task TryEnsureAgentRunningAsync_AgentRunning_DoesNothing()
    {
        agent.IsRunning().Returns(true);

        await Assert.That(await executor.TryEnsureAgentRunningAsync(TestData.Ct)).IsTrue();

        agent.StartAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
    }
}
