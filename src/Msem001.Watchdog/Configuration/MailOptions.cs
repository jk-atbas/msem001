namespace Msem001.Watchdog.Configuration;

/// <summary>
/// Mail alerting for log events of level <c>Error</c> and <c>Critical</c>.
/// </summary>
public sealed class MailOptions
{
    /// <summary>Whether mails are sent.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>SMTP host.</summary>
    public string Host { get; set; } = "in-v3.mailjet.com";

    /// <summary>SMTP port.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Use STARTTLS (required by Mailjet on port 587).</summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>SMTP user (Mailjet API key).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>SMTP password (Mailjet secret key).</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Sender address. Without <c>@</c> the <see cref="AddressSuffix"/> is appended.</summary>
    public string From { get; set; } = "msem001-watchdog";

    /// <summary>Suffix appended to addresses that contain no <c>@</c>.</summary>
    public string AddressSuffix { get; set; } = "@atbas.de";

    /// <summary>Recipients. Entries without <c>@</c> get the <see cref="AddressSuffix"/>.</summary>
    public IList<string> Recipients { get; set; } = [];

    /// <summary>Prefix of the mail subject.</summary>
    public string SubjectPrefix { get; set; } = "[MSEM001 Watchdog]";

    /// <summary>Events arriving within this window are combined into one mail.</summary>
    public TimeSpan BatchDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Maximum number of events per mail.</summary>
    public int MaxEntriesPerMail { get; set; } = 50;

    /// <summary>Number of send attempts per mail.</summary>
    public int SendAttempts { get; set; } = 3;

    /// <summary>Base delay between send attempts (multiplied with the attempt number).</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>SMTP timeout.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
