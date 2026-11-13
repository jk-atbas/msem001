using MimeKit;
using Msem001.Watchdog.Alerting;
using Msem001.Watchdog.Configuration;

namespace Msem001.Watchdog.Tests.Alerting;

public sealed class MailKitMailSenderTests
{
    [Test]
    public async Task BuildMessage_ResolvesAddressesWithSuffix()
    {
        var settings = new MailOptions { From = "msem001-watchdog", Recipients = ["j.doe", "ops@example.com"] };

        using var message = MailKitMailSender.BuildMessage(new AlertMail("Betreff", "Inhalt"), settings);

        await Assert.That(message.From.Mailboxes.Single().Address).IsEqualTo("msem001-watchdog@atbas.de");
        await Assert.That(string.Join(";", message.To.Mailboxes.Select(m => m.Address))).IsEqualTo("j.doe@atbas.de;ops@example.com");
        await Assert.That(message.Subject).IsEqualTo("Betreff");
        await Assert.That(message.TextBody).IsEqualTo("Inhalt");
        await Assert.That(message.Body).IsTypeOf<TextPart>();
    }
}
