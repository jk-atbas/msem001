using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;

namespace Msem001.Watchdog.Alerting;

/// <summary>
/// Logger provider that queues all <c>Error</c> and <c>Critical</c> events for the <see cref="MailAlertDispatcher"/>.
/// </summary>
/// <remarks>
/// Events of the alerting namespace itself are ignored, otherwise a failing SMTP server would cause a mail loop.
/// </remarks>
[ProviderAlias("MailAlert")]
internal sealed class MailAlertLoggerProvider(AlertQueue queue, IOptions<WatchdogOptions> options, TimeProvider timeProvider) : ILoggerProvider
{
    internal static readonly string ExcludedCategoryPrefix = typeof(MailAlertLoggerProvider).Namespace!;

    public ILogger CreateLogger(string categoryName) => new MailAlertLogger(categoryName, this);

    public void Dispose()
    {
    }

    internal bool IsEnabled(string category, LogLevel logLevel)
    {
        if (logLevel < LogLevel.Error || logLevel == LogLevel.None)
            return false;

        if (category.StartsWith(ExcludedCategoryPrefix, StringComparison.Ordinal))
            return false;

        try
        {
            return options.Value.Mail.Enabled;
        }
        catch (OptionsValidationException)
        {
            // Invalid configuration must never break logging; the host reports it on start.
            return false;
        }
    }

    internal void Enqueue(string category, LogLevel logLevel, EventId eventId, string message, Exception? exception) =>
        queue.TryEnqueue(new AlertEntry(timeProvider.GetUtcNow(), logLevel, category, eventId, message, exception?.ToString()));

    private sealed class MailAlertLogger(string category, MailAlertLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => provider.IsEnabled(category, logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            ArgumentNullException.ThrowIfNull(formatter);
            provider.Enqueue(category, logLevel, eventId, formatter(state, exception), exception);
        }
    }
}
