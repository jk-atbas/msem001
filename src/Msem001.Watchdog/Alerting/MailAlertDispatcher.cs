using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;

namespace Msem001.Watchdog.Alerting;

/// <summary>
/// Reads queued alerts, combines events of a short time window into one mail and sends it with retries.
/// </summary>
internal sealed partial class MailAlertDispatcher(
    AlertQueue queue,
    IMailSender mailSender,
    AlertMailComposer composer,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<MailAlertDispatcher> logger) : BackgroundService
{
    private static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(20);

    internal async Task SendBatchAsync(IReadOnlyList<AlertEntry> batch, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
            return;

        var settings = options.Value.Mail;
        var mail = composer.Compose(batch);

        for (var attempt = 1; attempt <= settings.SendAttempts; attempt++)
        {
            try
            {
                await mailSender.SendAsync(mail, cancellationToken).ConfigureAwait(false);
                LogMailSent(logger, batch.Count, mail.Subject);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogSendFailed(logger, ex, attempt, settings.SendAttempts);
                if (attempt < settings.SendAttempts)
                    await Task.Delay(settings.RetryDelay * attempt, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        // This category is excluded from mail alerting, the event only ends up in the log files.
        LogMailDropped(logger, batch.Count, mail.Subject);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<AlertEntry>();
        try
        {
            while (await queue.Reader.WaitToReadAsync(stoppingToken).ConfigureAwait(false))
            {
                var settings = options.Value.Mail;
                Drain(batch, settings.MaxEntriesPerMail);

                // Collect follow-up events (e.g. an error followed by a critical) into the same mail.
                if (batch.Count < settings.MaxEntriesPerMail && settings.BatchDelay > TimeSpan.Zero)
                    await Task.Delay(settings.BatchDelay, timeProvider, stoppingToken).ConfigureAwait(false);

                Drain(batch, settings.MaxEntriesPerMail);
                await SendBatchAsync(batch, stoppingToken).ConfigureAwait(false);
                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Service is stopping, flush below.
        }

        await FlushAsync(batch).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 9000, Level = LogLevel.Information, Message = "Alarm-Mail mit {Count} Meldung(en) versendet: {Subject}")]
    private static partial void LogMailSent(ILogger logger, int count, string subject);

    [LoggerMessage(EventId = 9001, Level = LogLevel.Warning, Message = "Versand der Alarm-Mail fehlgeschlagen (Versuch {Attempt}/{MaxAttempts})")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, int attempt, int maxAttempts);

    [LoggerMessage(EventId = 9002, Level = LogLevel.Error, Message = "Alarm-Mail mit {Count} Meldung(en) konnte nicht versendet werden und wird verworfen: {Subject}")]
    private static partial void LogMailDropped(ILogger logger, int count, string subject);

    private async Task FlushAsync(List<AlertEntry> batch)
    {
        Drain(batch, int.MaxValue);
        if (batch.Count == 0)
            return;

        using var timeout = new CancellationTokenSource(ShutdownFlushTimeout);
        try
        {
            await SendBatchAsync(batch, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogMailDropped(logger, batch.Count, "(Shutdown)");
        }
    }

    private void Drain(List<AlertEntry> batch, int maxEntries)
    {
        while (batch.Count < maxEntries && queue.Reader.TryRead(out var entry))
            batch.Add(entry);
    }
}
