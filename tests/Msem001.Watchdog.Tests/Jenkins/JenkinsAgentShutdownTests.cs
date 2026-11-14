using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Jenkins;
using Msem001.Watchdog.Maintenance;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Jenkins;

public sealed class JenkinsAgentShutdownTests : IDisposable
{
    private readonly Mock<IJenkinsAgent> agent = Mock.Of<IJenkinsAgent>();
    private readonly MockLogger<JenkinsAgentShutdown> logger = Mock.Logger<JenkinsAgentShutdown>();
    private readonly MaintenanceExecutor executor;

    public JenkinsAgentShutdownTests()
    {
        executor = new MaintenanceExecutor(agent.Object, Mock.Of<IJenkinsAgentDrain>().Object, Mock.Logger<MaintenanceExecutor>());
    }

    public void Dispose() => executor.Dispose();

    [Test]
    public async Task Stopping_BlocksAgentStartsAndStopTerminatesAgent()
    {
        var shutdown = CreateSut();
        agent.IsRunning().Returns(false);

        await RunLifecycleAsync(shutdown);

        agent.StopAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
        await Assert.That(await executor.TryEnsureAgentRunningAsync(TestData.Ct)).IsFalse();
        agent.StartAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("wird mit beendet").WasCalled(Times.Once);
    }

    [Test]
    public async Task StopWithServiceDisabled_AgentKeepsRunning()
    {
        var shutdown = CreateSut(o => o.JenkinsAgent.StopWithService = false);
        agent.IsRunning().Returns(false);

        await RunLifecycleAsync(shutdown);

        agent.StopAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        await Assert.That(await executor.TryEnsureAgentRunningAsync(TestData.Ct)).IsTrue();
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("bleibt verbunden").WasCalled(Times.Once);
    }

    [Test]
    public async Task AgentNotManaged_DoesNothing()
    {
        var shutdown = CreateSut(o => o.JenkinsAgent.Enabled = false);

        await RunLifecycleAsync(shutdown);

        agent.StopAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        await Assert.That(logger.Entries).IsEmpty();
    }

    [Test]
    public async Task StopFails_LogsError()
    {
        agent.StopAsync(Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("WMI"));

        await RunLifecycleAsync(CreateSut());

        logger.VerifyLog().AtLevel(LogLevel.Error).WithException<InvalidOperationException>().ContainingMessage("nicht beendet").WasCalled(Times.Once);
    }

    [Test]
    public async Task HostShutdown_StopsAgentAfterAllWorkers()
    {
        var order = new List<string>();
        agent.StopAsync(Arg.Any<CancellationToken>()).Callback(() => order.Add("agent stop"));
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(executor);
        builder.Services.AddSingleton(agent.Object);
        builder.Services.AddSingleton(TestData.CreateOptions());
        builder.Services.AddSingleton<ILogger<JenkinsAgentShutdown>>(logger);
        builder.Services.AddHostedService<JenkinsAgentShutdown>();
        builder.Services.AddHostedService(_ => new RecordingWorker(order));
        using var host = builder.Build();

        await host.StartAsync(TestData.Ct);
        await host.StopAsync(TestData.Ct);

        await Assert.That(string.Join(" > ", order)).IsEqualTo("worker stop > agent stop");
    }

    private static async Task RunLifecycleAsync(JenkinsAgentShutdown shutdown)
    {
        await shutdown.StartingAsync(TestData.Ct);
        await shutdown.StartAsync(TestData.Ct);
        await shutdown.StartedAsync(TestData.Ct);
        await shutdown.StoppingAsync(TestData.Ct);
        await shutdown.StopAsync(TestData.Ct);
        await shutdown.StoppedAsync(TestData.Ct);
    }

    private JenkinsAgentShutdown CreateSut(Action<WatchdogOptions>? configure = null) =>
        new(agent.Object, executor, TestData.CreateOptions(configure), logger);

    private sealed class RecordingWorker(List<string> order) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            order.Add("worker stop");
            return Task.CompletedTask;
        }
    }
}
