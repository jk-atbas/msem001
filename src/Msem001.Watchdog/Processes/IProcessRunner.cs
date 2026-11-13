namespace Msem001.Watchdog.Processes;

/// <summary>
/// Runs external processes to completion and captures their output.
/// </summary>
internal interface IProcessRunner
{
    /// <summary>
    /// Runs the process. On timeout the process tree is killed and <see cref="ProcessRunResult.TimedOut"/> is set.
    /// On cancellation the process tree is killed and an <see cref="OperationCanceledException"/> is thrown.
    /// </summary>
    /// <returns>The result including the captured output.</returns>
    Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken);
}
