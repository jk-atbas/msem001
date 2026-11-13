namespace Msem001.Watchdog.Processes;

/// <summary>
/// A long-running process started by the service.
/// </summary>
internal interface IRunningProcess : IDisposable
{
    int Id { get; }

    bool HasExited { get; }

    int? ExitCode { get; }
}
