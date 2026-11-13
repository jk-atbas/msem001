using System.Globalization;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Alerting;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Alerting;

public sealed class MailAlertLoggerProviderTests
{
    private readonly AlertQueue queue = new();

    [Test]
    public async Task Log_ErrorAndCritical_AreQueued()
    {
        using var provider = new MailAlertLoggerProvider(queue, TestData.CreateOptions(), TimeProvider.System);
        var logger = provider.CreateLogger("Msem001.Watchdog.Podman.PodmanRecovery");

        logger.Log(LogLevel.Information, "info");
        logger.Log(LogLevel.Warning, "warn");
        logger.Log(LogLevel.Error, new EventId(7), new InvalidOperationException("boom"), "Fehler {Value}", 42);
        logger.Log(LogLevel.Critical, "kritisch");

        var entries = ReadAll();
        await Assert.That(string.Join(",", entries.Select(e => e.Level))).IsEqualTo("Error,Critical");
        await Assert.That(entries[0].Message).IsEqualTo("Fehler 42");
        await Assert.That(entries[0].EventId.Id).IsEqualTo(7);
        await Assert.That(entries[0].Exception).Contains("boom");
        await Assert.That(entries[0].Category).IsEqualTo("Msem001.Watchdog.Podman.PodmanRecovery");
    }

    [Test]
    public async Task Log_AlertingCategory_IsIgnoredToAvoidMailLoops()
    {
        using var provider = new MailAlertLoggerProvider(queue, TestData.CreateOptions(), TimeProvider.System);

        provider.CreateLogger(typeof(MailAlertDispatcher).FullName!).Log(LogLevel.Error, "smtp down");

        await Assert.That(ReadAll()).IsEmpty();
    }

    [Test]
    public async Task Log_MailDisabled_IsIgnored()
    {
        using var provider = new MailAlertLoggerProvider(queue, TestData.CreateOptions(o => o.Mail.Enabled = false), TimeProvider.System);

        provider.CreateLogger("x").Log(LogLevel.Critical, "x");

        await Assert.That(ReadAll()).IsEmpty();
    }

    [Test]
    public async Task Log_InvalidOptions_DoesNotThrow()
    {
        var options = Mock.Of<IOptions<WatchdogOptions>>();
        options.Value.Throws(new OptionsValidationException("Watchdog", typeof(WatchdogOptions), ["invalid"]));
        using var provider = new MailAlertLoggerProvider(queue, options.Object, TimeProvider.System);

        await Assert.That(() => provider.CreateLogger("x").Log(LogLevel.Critical, "x")).ThrowsNothing();
        await Assert.That(ReadAll()).IsEmpty();
    }

    [Test]
    public async Task Logger_HasNoScopesAndIgnoresNone()
    {
        using var provider = new MailAlertLoggerProvider(queue, TestData.CreateOptions(), TimeProvider.System);
        var logger = provider.CreateLogger("x");

        await Assert.That(logger.BeginScope("scope")).IsNull();
        await Assert.That(logger.IsEnabled(LogLevel.None)).IsFalse();
    }

    [Test]
    public async Task Queue_WhenFull_DropsOldest()
    {
        for (var i = 0; i < AlertQueue.Capacity + 10; i++)
            queue.TryEnqueue(new AlertEntry(DateTimeOffset.UnixEpoch, LogLevel.Error, "x", default, i.ToString(CultureInfo.InvariantCulture), null));

        var entries = ReadAll();
        await Assert.That(entries.Count).IsEqualTo(AlertQueue.Capacity);
        await Assert.That(entries[0].Message).IsEqualTo("10");
    }

    private List<AlertEntry> ReadAll()
    {
        var entries = new List<AlertEntry>();
        while (queue.Reader.TryRead(out var entry))
            entries.Add(entry);

        return entries;
    }
}
