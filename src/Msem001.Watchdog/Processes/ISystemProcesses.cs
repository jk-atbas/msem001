using System.Diagnostics;

namespace Msem001.Watchdog.Processes;

/// <summary>
/// Access to the processes of the machine.
/// </summary>
internal interface ISystemProcesses
{
    /// <summary>Finds processes by image name (e.g. <c>java.exe</c>) including their command line.</summary>
    /// <returns>The matching processes.</returns>
    IReadOnlyList<ProcessSnapshot> FindByImageName(string imageName);

    /// <summary>Counts processes by name without extension (e.g. <c>win-sshproxy</c>).</summary>
    /// <returns>The number of processes.</returns>
    int CountByName(string processName);

    /// <summary>Kills the process and its children.</summary>
    /// <returns><see langword="false"/> if it did not exit within the timeout.</returns>
    Task<bool> KillTreeAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Starts a long-running process and forwards every output line to <paramref name="onOutput"/>.</summary>
    /// <returns>A handle of the started process.</returns>
    IRunningProcess Start(ProcessStartInfo startInfo, Action<string> onOutput);
}
