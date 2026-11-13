using System.Diagnostics;

namespace Msem001.Watchdog.Processes.Abstractions;

/// <summary>
/// Testable abstraction of the static members of <c>System.Diagnostics.Process</c>.
/// </summary>
internal interface IProcessFactory
{
    /// <summary>Starts a process. Output events are raised after <see cref="IProcess.BeginOutputReadLine"/>.</summary>
    /// <returns>The started process.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">The executable could not be started.</exception>
    IProcess Start(ProcessStartInfo startInfo);

    /// <summary>Gets a running process.</summary>
    /// <returns>The process or <see langword="null"/> if no process with this id is running.</returns>
    IProcess? GetProcessById(int processId);

    /// <summary>Gets all processes with the given name (without extension).</summary>
    /// <returns>The processes; the caller disposes them.</returns>
    IReadOnlyList<IProcess> GetProcessesByName(string processName);
}
