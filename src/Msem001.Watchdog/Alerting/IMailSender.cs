namespace Msem001.Watchdog.Alerting;

/// <summary>
/// Sends an <see cref="AlertMail"/> to the configured recipients.
/// </summary>
internal interface IMailSender
{
    Task SendAsync(AlertMail mail, CancellationToken cancellationToken);
}
