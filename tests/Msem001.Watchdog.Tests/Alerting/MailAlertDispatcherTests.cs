using System.Collections.Concurrent;
using Msem001.Watchdog.Alerting;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Disk;
using Msem001.Watchdog.Memory;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Alerting;

public sealed class MailAlertDispatcherTests
{
    private readonly AlertQueue queue = new();
    private readonly Mock<IMailSender> sender = Mock.Of<IMailSender>();
    private readonly ConcurrentQueue<AlertMail> sentMails = new();
    private readonly MockLogger<MailAlertDispatcher> logger = Mock.Logger<MailAlertDispatcher>();

    public MailAlertDispatcherTests()
    {
        sender.SendAsync(Arg.Any<AlertMail>(), Arg.Any<CancellationToken>()).Callback((mail, _) => sentMails.Enqueue(mail));
    }

    [Test]
    public async Task SendBatchAsync_Success_SendsOneMailWithAllEntries()
    {
        using var dispatcher = CreateDispatcher();

        await dispatcher.SendBatchAsync([Entry("Fehler A"), Entry("Fehler B")], TestData.Ct);

        await Assert.That(sentMails.Count).IsEqualTo(1);
        await Assert.That(sentMails.Single().Body).Contains("Fehler A").And.Contains("Fehler B");
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("2 Meldung(en) versendet").WasCalled(Times.Once);
    }

    [Test]
    public async Task SendBatchAsync_TransientFailure_IsRetried()
    {
        var attempts = 0;
        sender.SendAsync(Arg.Any<AlertMail>(), Arg.Any<CancellationToken>()).Returns((_, _) =>
            Interlocked.Increment(ref attempts) == 1 ? Task.FromException(new IOException("smtp")) : Task.CompletedTask);
        using var dispatcher = CreateDispatcher();

        await dispatcher.SendBatchAsync([Entry("x")], TestData.Ct);

        await Assert.That(attempts).IsEqualTo(2);
        logger.VerifyLog().AtLevel(LogLevel.Warning).WithException<IOException>().WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Information).WasCalled(Times.Once);
    }

    [Test]
    public async Task SendBatchAsync_PermanentFailure_GivesUpAfterConfiguredAttempts()
    {
        sender.SendAsync(Arg.Any<AlertMail>(), Arg.Any<CancellationToken>()).Throws(new IOException("smtp"));
        using var dispatcher = CreateDispatcher();

        await dispatcher.SendBatchAsync([Entry("x")], TestData.Ct);

        sender.SendAsync(Arg.Any<AlertMail>(), Arg.Any<CancellationToken>()).WasCalled(Times.Exactly(3));
        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("verworfen").WasCalled(Times.Once);
    }

    [Test]
    public async Task SendBatchAsync_EmptyBatch_SendsNothing()
    {
        using var dispatcher = CreateDispatcher();

        await dispatcher.SendBatchAsync([], TestData.Ct);

        sender.SendAsync(Arg.Any<AlertMail>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    public async Task Dispatcher_SendsQueuedAlertsInBackground()
    {
        using var dispatcher = CreateDispatcher();

        await dispatcher.StartAsync(TestData.Ct);
        queue.TryEnqueue(Entry("Hintergrund"));
        await Wait.UntilAsync(() => !sentMails.IsEmpty);
        await dispatcher.StopAsync(TestData.Ct);

        await Assert.That(sentMails.Single().Body).Contains("Hintergrund");
    }

    [Test]
    public async Task Dispatcher_RespectsMaxEntriesPerMail()
    {
        for (var i = 0; i < 5; i++)
            queue.TryEnqueue(Entry($"Meldung {i}"));

        using var dispatcher = CreateDispatcher(o => o.Mail.MaxEntriesPerMail = 2);
        await dispatcher.StartAsync(TestData.Ct);
        await Wait.UntilAsync(() => sentMails.Count == 3);
        await dispatcher.StopAsync(TestData.Ct);

        await Assert.That(sentMails.Select(m => m.Body)).All().Satisfy(body => body.Contains("Meldung"));
    }

    [Test]
    public async Task Dispatcher_OnShutdown_FlushesPendingAlerts()
    {
        // A long batch window keeps the alert pending until the service stops.
        using var dispatcher = CreateDispatcher(o => o.Mail.BatchDelay = TimeSpan.FromHours(1));
        await dispatcher.StartAsync(TestData.Ct);
        queue.TryEnqueue(Entry("beim Beenden"));
        await Task.Delay(200, TestData.Ct);

        await dispatcher.StopAsync(TestData.Ct);

        await Assert.That(sentMails.Count).IsEqualTo(1);
        await Assert.That(sentMails.Single().Body).Contains("beim Beenden");
    }

    private static AlertEntry Entry(string message) =>
        new(DateTimeOffset.UnixEpoch, LogLevel.Error, "Test", default, message, null);

    private MailAlertDispatcher CreateDispatcher(Action<WatchdogOptions>? configure = null)
    {
        var options = TestData.CreateOptions(configure);
        var disk = Mock.Of<IDiskSpaceProbe>();
        disk.GetDiskSpace().Returns(TestData.Disk(freeGb: 100));
        var memory = Mock.Of<IMemoryProbe>();
        memory.GetMemory().Returns(TestData.Memory(usedPercent: 50));
        var composer = new AlertMailComposer(disk.Object, memory.Object, TestData.CreateEnvironment(), options, TimeProvider.System);
        return new MailAlertDispatcher(queue, sender.Object, composer, options, TimeProvider.System, logger);
    }
}
