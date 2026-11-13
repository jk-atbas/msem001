using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Disk;

namespace Msem001.Watchdog.Alerting;

/// <summary>
/// Builds the alert mail. Every mail contains the current free disk space.
/// </summary>
internal sealed class AlertMailComposer(
    IDiskSpaceProbe diskSpaceProbe,
    IEnvironmentProvider environment,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider)
{
    private const int MaxSubjectMessageLength = 120;

    public AlertMail Compose(IReadOnlyList<AlertEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (entries.Count == 0)
            throw new ArgumentException("Mindestens ein Eintrag erforderlich.", nameof(entries));

        var settings = options.Value.Mail;
        var highest = entries.MaxBy(e => e.Level)!;
        var disk = TryGetDiskSpace(out var diskError);
        var diskText = disk?.ToDisplayString() ?? $"unbekannt ({diskError})";

        var subject = new StringBuilder()
            .Append(settings.SubjectPrefix).Append(' ')
            .Append(LevelName(highest.Level)).Append(": ")
            .Append(Truncate(FirstLine(highest.Message), MaxSubjectMessageLength));
        if (entries.Count > 1)
            subject.Append(CultureInfo.InvariantCulture, $" (+{entries.Count - 1} weitere)");
        if (disk is not null)
            subject.Append(CultureInfo.InvariantCulture, $" | {disk.FreeGigabytes:F1} GB frei");

        var localTimeZone = timeProvider.LocalTimeZone;
        var body = new StringBuilder()
            .AppendLine(CultureInfo.InvariantCulture, $"Watchdog-Meldung von {environment.MachineName}")
            .AppendLine(CultureInfo.InvariantCulture, $"Zeitpunkt:            {FormatTimestamp(timeProvider.GetUtcNow(), localTimeZone)}")
            .AppendLine(CultureInfo.InvariantCulture, $"Freier Speicherplatz: {diskText}")
            .AppendLine()
            .AppendLine(CultureInfo.InvariantCulture, $"{entries.Count} Meldung(en):");

        foreach (var entry in entries)
        {
            body.AppendLine()
                .AppendLine("------------------------------------------------------------")
                .AppendLine(CultureInfo.InvariantCulture, $"[{FormatTimestamp(entry.Timestamp, localTimeZone)}] {LevelName(entry.Level)} {entry.Category} (EventId {entry.EventId.Id})")
                .AppendLine(entry.Message);

            if (!string.IsNullOrWhiteSpace(entry.Exception))
                body.AppendLine().AppendLine(entry.Exception);
        }

        return new AlertMail(subject.ToString(), body.ToString());
    }

    internal static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Critical => "CRITICAL",
        LogLevel.Error => "ERROR",
        LogLevel.Warning => "WARNING",
        _ => level.ToString().ToUpperInvariant(),
    };

    private static string FormatTimestamp(DateTimeOffset timestamp, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(timestamp, timeZone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

    private static string FirstLine(string message)
    {
        var index = message.IndexOfAny(['\r', '\n']);
        return index < 0 ? message : message[..index];
    }

    private static string Truncate(string value, int maxLength) => value.Length <= maxLength ? value : string.Concat(value.AsSpan(0, maxLength - 3), "...");

    private DiskSpaceInfo? TryGetDiskSpace(out string? error)
    {
        try
        {
            error = null;
            return diskSpaceProbe.GetDiskSpace();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = ex.Message;
            return null;
        }
    }
}
