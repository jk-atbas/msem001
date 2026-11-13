using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Msem001.Watchdog.Configuration;

namespace Msem001.Watchdog.Alerting;

/// <summary>
/// <see cref="IMailSender"/> via SMTP (Mailjet, port 587, STARTTLS).
/// </summary>
internal sealed class MailKitMailSender(IOptions<WatchdogOptions> options) : IMailSender
{
    public async Task SendAsync(AlertMail mail, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(mail);

        var settings = options.Value.Mail;
        using var message = BuildMessage(mail, settings);
        using var client = new SmtpClient { Timeout = (int)settings.Timeout.TotalMilliseconds };

        var security = settings.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(settings.Host, settings.Port, security, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(settings.Username))
            await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken).ConfigureAwait(false);

        await client.SendAsync(new SendRequest(message), cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }

    internal static MimeMessage BuildMessage(AlertMail mail, MailOptions settings)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(MailAddressResolver.Resolve(settings.From, settings.AddressSuffix)));
        foreach (var recipient in MailAddressResolver.ResolveAll(settings.Recipients, settings.AddressSuffix))
            message.To.Add(MailboxAddress.Parse(recipient));

        message.Subject = mail.Subject;
        message.Body = new TextPart("plain") { Text = mail.Body };
        return message;
    }
}
