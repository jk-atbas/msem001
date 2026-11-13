namespace Msem001.Watchdog.Processes;

/// <summary>
/// A running process including its command line.
/// </summary>
internal sealed record ProcessSnapshot(int ProcessId, string Name, string? CommandLine);
