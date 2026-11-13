using Microsoft.Extensions.Time.Testing;
using Msem001.Watchdog.Alerting;
using Msem001.Watchdog.Disk;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Alerting;

public sealed class AlertMailComposerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 20, 45, 0, TimeSpan.Zero);

    private readonly Mock<IDiskSpaceProbe> disk = Mock.Of<IDiskSpaceProbe>();
    private readonly AlertMailComposer composer;

    public AlertMailComposerTests()
    {
        var time = new FakeTimeProvider(Now);
        time.SetLocalTimeZone(TimeZoneInfo.Utc);
        disk.GetDiskSpace().Returns(TestData.Disk(freeGb: 42.5));
        composer = new AlertMailComposer(disk.Object, TestData.CreateEnvironment(), TestData.CreateOptions(), time);
    }

    [Test]
    public async Task Compose_SingleEntry_SubjectContainsLevelMessageAndFreeSpace()
    {
        var mail = composer.Compose([Entry(LogLevel.Error, "Speicherplatz knapp")]);

        await Assert.That(mail.Subject).IsEqualTo("[MSEM001 Watchdog] ERROR: Speicherplatz knapp | 42.5 GB frei");
    }

    [Test]
    public async Task Compose_Body_ContainsMachineDiskSpaceMessagesAndExceptions()
    {
        var mail = composer.Compose(
        [
            Entry(LogLevel.Error, "Skript fehlgeschlagen"),
            Entry(LogLevel.Critical, "Podman kaputt", "System.InvalidOperationException: boom"),
        ]);

        await Assert.That(mail.Body)
            .Contains("Watchdog-Meldung von MSEM001")
            .And.Contains(@"Freier Speicherplatz: C:\ 42.5 GB frei von 1000.0 GB (95.8 % belegt)")
            .And.Contains("2 Meldung(en)")
            .And.Contains("Skript fehlgeschlagen")
            .And.Contains("CRITICAL Test.Category (EventId 4711)")
            .And.Contains("System.InvalidOperationException: boom")
            .And.Contains("2026-11-13 20:45:00 +00:00");
    }

    [Test]
    public async Task Compose_MultipleEntries_SubjectUsesHighestLevelAndCount()
    {
        var mail = composer.Compose([Entry(LogLevel.Error, "erst"), Entry(LogLevel.Critical, "dann kritisch\nZeile 2"), Entry(LogLevel.Error, "noch")]);

        await Assert.That(mail.Subject).StartsWith("[MSEM001 Watchdog] CRITICAL: dann kritisch (+2 weitere)");
    }

    [Test]
    public async Task Compose_LongMessage_IsTruncatedInSubjectOnly()
    {
        var mail = composer.Compose([Entry(LogLevel.Error, new string('x', 300))]);

        await Assert.That(mail.Subject).Contains(new string('x', 117) + "...");
        await Assert.That(mail.Body).Contains(new string('x', 300));
    }

    [Test]
    public async Task Compose_DiskProbeFails_StillComposesMail()
    {
        disk.GetDiskSpace().Throws(new IOException("Laufwerk weg"));

        var mail = composer.Compose([Entry(LogLevel.Error, "x")]);

        await Assert.That(mail.Subject).DoesNotContain("GB frei");
        await Assert.That(mail.Body).Contains("unbekannt (Laufwerk weg)");
    }

    [Test]
    public async Task Compose_NoEntries_Throws()
    {
        await Assert.That(() => composer.Compose([])).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(LogLevel.Critical, "CRITICAL")]
    [Arguments(LogLevel.Error, "ERROR")]
    [Arguments(LogLevel.Warning, "WARNING")]
    [Arguments(LogLevel.Information, "INFORMATION")]
    public async Task LevelName_MapsLevels(LogLevel level, string expected)
    {
        await Assert.That(AlertMailComposer.LevelName(level)).IsEqualTo(expected);
    }

    private static AlertEntry Entry(LogLevel level, string message, string? exception = null) =>
        new(Now, level, "Test.Category", new EventId(4711), message, exception);
}
