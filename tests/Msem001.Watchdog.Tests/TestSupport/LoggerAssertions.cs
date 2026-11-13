namespace Msem001.Watchdog.Tests.TestSupport;

/// <summary>
/// Small helpers around <see cref="MockLogger"/> (TUnit.Mocks.Logging).
/// </summary>
internal static class LoggerAssertions
{
    public static LogEntry Latest(this MockLogger logger) =>
        logger.LatestEntry ?? throw new InvalidOperationException("Es wurde nichts geloggt.");

    public static int CountAtLeast(this MockLogger logger, LogLevel level) =>
        logger.Entries.Count(e => e.LogLevel >= level);

    public static bool HasEntry(this MockLogger logger, LogLevel level, string messagePart) =>
        logger.Entries.Any(e => e.LogLevel == level && e.Message.Contains(messagePart, StringComparison.Ordinal));
}
