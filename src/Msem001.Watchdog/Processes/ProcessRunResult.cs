namespace Msem001.Watchdog.Processes;

/// <summary>
/// Result of a <see cref="ProcessRunRequest"/>.
/// </summary>
internal sealed record ProcessRunResult(
    int ExitCode,
    bool TimedOut,
    IReadOnlyList<string> StandardOutput,
    IReadOnlyList<string> StandardError,
    TimeSpan Duration)
{
    public bool Succeeded => !TimedOut && ExitCode == 0;

    public string StandardOutputText => string.Join('\n', StandardOutput);

    public string StandardErrorText => string.Join('\n', StandardError);
}
