namespace Msem001.Watchdog.Alerting;

/// <summary>
/// A log event that is reported by mail.
/// </summary>
internal sealed record AlertEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    EventId EventId,
    string Message,
    string? Exception);
