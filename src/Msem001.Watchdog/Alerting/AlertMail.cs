namespace Msem001.Watchdog.Alerting;

/// <summary>
/// A composed alert mail.
/// </summary>
internal sealed record AlertMail(string Subject, string Body);
