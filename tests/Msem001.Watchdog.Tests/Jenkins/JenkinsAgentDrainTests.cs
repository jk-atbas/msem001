using Microsoft.Extensions.Time.Testing;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Jenkins;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Jenkins;

public sealed class JenkinsAgentDrainTests
{
    private static readonly JenkinsNodeStatus OnlineBusy = new(false, false, false, null);
    private static readonly JenkinsNodeStatus OnlineIdle = new(false, false, true, null);
    private static readonly JenkinsNodeStatus OwnOfflineBusy = new(true, true, false, $"{JenkinsAgentDrain.OfflineMarker} Wartung");
    private static readonly JenkinsNodeStatus OwnOfflineIdle = new(true, true, true, $"{JenkinsAgentDrain.OfflineMarker} Wartung");
    private static readonly JenkinsNodeStatus ForeignOfflineIdle = new(true, true, true, "Hardware-Tausch");

    private readonly Mock<IJenkinsApi> api = Mock.Of<IJenkinsApi>();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 13, 8, 0, 0, TimeSpan.Zero));
    private readonly MockLogger<JenkinsAgentDrain> logger = Mock.Logger<JenkinsAgentDrain>();
    private readonly List<string> toggles = [];
    private readonly JenkinsAgentDrain drain;

    public JenkinsAgentDrainTests()
    {
        api.IsConfigured.Returns(true);
        api.ToggleOfflineAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Callback((message, _) => toggles.Add(message is null ? "online" : "offline"));
        drain = new JenkinsAgentDrain(api.Object, TestData.CreateOptions(), time, logger);
    }

    [Test]
    public async Task DrainAsync_NotConfigured_DoesNotCallApi()
    {
        api.IsConfigured.Returns(false);

        await Assert.That(await drain.DrainAsync("Test", null, TestData.Ct)).IsFalse();

        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("Kein Jenkins-API-Token").WasCalled(Times.Once);
    }

    [Test]
    public async Task DrainAsync_UrgentFromStart_DoesNotMarkOffline()
    {
        await Assert.That(await drain.DrainAsync("Test", () => true, TestData.Ct)).IsFalse();

        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("dringend").WasCalled(Times.Once);
    }

    [Test]
    public async Task DrainAsync_Idle_MarksOfflineWithMarkerAndReturnsImmediately()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Returns(OnlineIdle);
        string? message = null;
        api.ToggleOfflineAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Callback((m, _) => message = m);

        await Assert.That(await drain.DrainAsync("Speicherbereinigung", null, TestData.Ct)).IsTrue();

        await Assert.That(message).StartsWith(JenkinsAgentDrain.OfflineMarker).And.Contains("Speicherbereinigung");
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
        logger.VerifyLog().ContainingMessage("wartet auf laufende Builds").WasNeverCalled();
    }

    [Test]
    public async Task DrainAsync_BuildRunning_WaitsUntilIdle()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(OnlineBusy, OwnOfflineBusy, OwnOfflineBusy, OwnOfflineIdle);

        var task = drain.DrainAsync("Test", () => false, TestData.Ct);
        await Wait.UntilAsync(() => task.IsCompleted, time, TimeSpan.FromSeconds(30));

        await Assert.That(await task).IsTrue();
        await Assert.That(toggles).IsEquivalentTo(["offline"]);
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(4));
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("wartet auf laufende Builds (höchstens 01:00:00").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("Agent ist frei nach").WasCalled(Times.Once);
    }

    [Test]
    public async Task DrainAsync_BuildRunsLongerThanTimeout_GivesUp()
    {
        var (localDrain, localLogger) = CreateDrain(o => o.JenkinsAgent.DrainTimeout = TimeSpan.FromMinutes(2));
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(OnlineBusy, OwnOfflineBusy);

        var task = localDrain.DrainAsync("Test", null, TestData.Ct);
        await Wait.UntilAsync(() => task.IsCompleted, time, TimeSpan.FromSeconds(30));

        await Assert.That(await task).IsTrue();
        localLogger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("läuft noch ein Build").WasCalled(Times.Once);
    }

    [Test]
    public async Task DrainAsync_ZeroTimeout_DoesNotWait()
    {
        var (localDrain, _) = CreateDrain(o => o.JenkinsAgent.DrainTimeout = TimeSpan.Zero);
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Returns(OnlineBusy);

        await Assert.That(await localDrain.DrainAsync("Test", null, TestData.Ct)).IsTrue();

        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task DrainAsync_BecomesUrgentWhileWaiting_StopsWaiting()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(OnlineBusy, OwnOfflineBusy);
        var checks = 0;

        var task = drain.DrainAsync("Test", () => ++checks > 3, TestData.Ct);
        await Wait.UntilAsync(() => task.IsCompleted, time, TimeSpan.FromSeconds(30));

        await Assert.That(await task).IsTrue();
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("während des Wartens dringend").WasCalled(Times.Once);
    }

    [Test]
    public async Task DrainAsync_OfflineByPerson_WaitsButDoesNotRestore()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Returns(ForeignOfflineIdle);

        await Assert.That(await drain.DrainAsync("Test", null, TestData.Ct)).IsFalse();

        await Assert.That(toggles).IsEmpty();
        logger.VerifyLog().ContainingMessage("bereits offline geschaltet: Hardware-Tausch").WasCalled(Times.Once);
    }

    [Test]
    public async Task DrainAsync_LeftOverOwnMark_IsRestoredAfterwards()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Returns(OwnOfflineIdle);

        await Assert.That(await drain.DrainAsync("Test", null, TestData.Ct)).IsTrue();

        await Assert.That(toggles).IsEmpty();
    }

    [Test]
    public async Task DrainAsync_ApiUnavailable_LogsErrorAndDoesNotWait()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Throws(new HttpRequestException("connection refused"));

        await Assert.That(await drain.DrainAsync("Test", null, TestData.Ct)).IsFalse();

        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("Jenkins-API nicht nutzbar").WithException<HttpRequestException>().WasCalled(Times.Once);
    }

    [Test]
    public async Task DrainAsync_ApiFailsAfterMarkingOffline_ReportsMarkSoItGetsRestored()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Returns(OnlineBusy);
        api.ToggleOfflineAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Callback(() => api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Throws(new HttpRequestException("weg")));

        var task = drain.DrainAsync("Test", null, TestData.Ct);
        await Wait.UntilAsync(() => task.IsCompleted, time, TimeSpan.FromSeconds(30));

        await Assert.That(await task).IsTrue();
        logger.VerifyLog().AtLevel(LogLevel.Error).WithException<HttpRequestException>().WasCalled(Times.Once);
    }

    [Test]
    public async Task DrainAsync_HttpTimeout_IsTreatedAsApiFailure()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Throws(new TaskCanceledException("timeout", new TimeoutException()));

        await Assert.That(await drain.DrainAsync("Test", null, TestData.Ct)).IsFalse();

        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("Jenkins-API nicht nutzbar").WasCalled(Times.Once);
    }

    [Test]
    public async Task DrainAsync_ServiceStopsWhileWaiting_BringsNodeBackOnline()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).ReturnsSequentially(OnlineBusy, OwnOfflineBusy);
        using var stopping = new CancellationTokenSource();

        var task = drain.DrainAsync("Test", null, stopping.Token);
        await Wait.UntilAsync(() => toggles.Count == 1);
        await stopping.CancelAsync();

        await Assert.That(async () => { await task; }).Throws<OperationCanceledException>();
        await Assert.That(toggles).IsEquivalentTo(["offline", "online"]);
    }

    [Test]
    public async Task RestoreAsync_NotMarked_DoesNothing()
    {
        await drain.RestoreAsync(false, TestData.Ct);

        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task RestoreAsync_StillOffline_BringsOnline()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Returns(OwnOfflineIdle);

        await drain.RestoreAsync(true, TestData.Ct);

        await Assert.That(toggles).IsEquivalentTo(["online"]);
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("wieder online").WasCalled(Times.Once);
    }

    [Test]
    public async Task RestoreAsync_AlreadyOnline_DoesNotToggleOfflineAgain()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Returns(OnlineIdle);

        await drain.RestoreAsync(true, TestData.Ct);

        await Assert.That(toggles).IsEmpty();
    }

    [Test]
    public async Task RestoreAsync_Fails_LogsErrorAndStaleCheckRetries()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Throws(new HttpRequestException("weg"));
        await drain.ReleaseStaleOfflineAsync(TestData.Ct); // start check fails, stays pending

        await drain.RestoreAsync(true, TestData.Ct);
        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("nicht wieder online geschaltet").WasCalled(Times.Once);

        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Returns(OwnOfflineIdle);
        await drain.ReleaseStaleOfflineAsync(TestData.Ct);

        await Assert.That(toggles).IsEquivalentTo(["online"]);
    }

    [Test]
    public async Task ReleaseStaleOfflineAsync_OwnMark_BringsOnlineOnlyOnce()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Returns(OwnOfflineIdle);

        await drain.ReleaseStaleOfflineAsync(TestData.Ct);
        await drain.ReleaseStaleOfflineAsync(TestData.Ct);

        await Assert.That(toggles).IsEquivalentTo(["online"]);
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("noch vom Watchdog offline geschaltet").WasCalled(Times.Once);
    }

    [Test]
    public async Task ReleaseStaleOfflineAsync_ForeignMark_KeepsNodeOffline()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Returns(ForeignOfflineIdle);

        await drain.ReleaseStaleOfflineAsync(TestData.Ct);

        await Assert.That(toggles).IsEmpty();
    }

    [Test]
    public async Task ReleaseStaleOfflineAsync_NotConfigured_DoesNotCallApi()
    {
        api.IsConfigured.Returns(false);

        await drain.ReleaseStaleOfflineAsync(TestData.Ct);

        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task ReleaseStaleOfflineAsync_ApiFails_LogsWarningAndRetriesLater()
    {
        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).Throws(new HttpRequestException("weg"));

        await drain.ReleaseStaleOfflineAsync(TestData.Ct);
        await drain.ReleaseStaleOfflineAsync(TestData.Ct);

        api.GetNodeStatusAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(2));
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("nicht geprüft werden").WasCalled(Times.Exactly(2));
    }

    private (JenkinsAgentDrain Drain, MockLogger<JenkinsAgentDrain> Logger) CreateDrain(Action<WatchdogOptions> configure)
    {
        var localLogger = Mock.Logger<JenkinsAgentDrain>();
        return (new JenkinsAgentDrain(api.Object, TestData.CreateOptions(configure), time, localLogger), localLogger);
    }
}
