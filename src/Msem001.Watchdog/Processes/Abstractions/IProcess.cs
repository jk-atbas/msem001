namespace Msem001.Watchdog.Processes.Abstractions;

/// <summary>
/// Testable abstraction of a <c>System.Diagnostics.Process</c>, limited to the members the watchdog uses.
/// </summary>
/// <remarks>
/// There is no maintained NuGet package that wraps the modern Process API
/// (<c>Kill(entireProcessTree)</c>, <c>WaitForExitAsync</c>, <c>ArgumentList</c>), so this thin
/// abstraction mirrors the relevant members 1:1.
/// </remarks>
internal interface IProcess : IDisposable
{
    /// <summary>Raised for every line of the standard output; <see cref="ProcessLineEventArgs.Line"/> is <see langword="null"/> at the end of the stream.</summary>
    event EventHandler<ProcessLineEventArgs>? OutputLineReceived;

    /// <summary>Raised for every line of the standard error; <see cref="ProcessLineEventArgs.Line"/> is <see langword="null"/> at the end of the stream.</summary>
    event EventHandler<ProcessLineEventArgs>? ErrorLineReceived;

    /// <summary>Raised when the process exited.</summary>
    event EventHandler? Exited;

    int Id { get; }

    bool HasExited { get; }

    /// <summary>Exit code; only valid after <see cref="HasExited"/>.</summary>
    int ExitCode { get; }

    void BeginOutputReadLine();

    void BeginErrorReadLine();

    /// <summary>Kills the process (and optionally all of its children).</summary>
    void Kill(bool entireProcessTree);

    /// <summary>Waits until the process exited.</summary>
    /// <returns>A task that completes when the process exited.</returns>
    Task WaitForExitAsync(CancellationToken cancellationToken);
}
