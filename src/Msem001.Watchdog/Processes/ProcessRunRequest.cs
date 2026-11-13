namespace Msem001.Watchdog.Processes;

/// <summary>
/// Describes an external process that is run to completion.
/// </summary>
internal sealed record ProcessRunRequest(string FileName, IReadOnlyList<string> Arguments)
{
    public string? WorkingDirectory { get; init; }

    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Name used in log events.</summary>
    public string DisplayName { get; init; } = FileName;

    /// <summary>Log level of the forwarded standard output lines (standard error uses at least <c>Warning</c>).</summary>
    public LogLevel OutputLogLevel { get; init; } = LogLevel.Debug;
}
