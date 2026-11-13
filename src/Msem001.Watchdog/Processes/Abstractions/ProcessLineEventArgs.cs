namespace Msem001.Watchdog.Processes.Abstractions;

/// <summary>
/// One line of process output (the BCL <see cref="System.Diagnostics.DataReceivedEventArgs"/> cannot be constructed in tests).
/// </summary>
internal sealed class ProcessLineEventArgs(string? line) : EventArgs
{
    /// <summary>The line, or <see langword="null"/> at the end of the stream.</summary>
    public string? Line { get; } = line;
}
