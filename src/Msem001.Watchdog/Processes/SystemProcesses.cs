using System.ComponentModel;
using System.Diagnostics;
using Msem001.Watchdog.Processes.Abstractions;

namespace Msem001.Watchdog.Processes;

/// <summary>
/// <see cref="ISystemProcesses"/> based on <see cref="IProcessQuery"/> (command lines) and <see cref="IProcessFactory"/>.
/// </summary>
internal sealed class SystemProcesses(IProcessFactory processFactory, IProcessQuery processQuery) : ISystemProcesses
{
    public IReadOnlyList<ProcessSnapshot> FindByImageName(string imageName) => processQuery.FindByImageName(imageName);

    public int CountByName(string processName)
    {
        var processes = processFactory.GetProcessesByName(processName);
        foreach (var process in processes)
            process.Dispose();

        return processes.Count;
    }

    public async Task<bool> KillTreeAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var process = processFactory.GetProcessById(processId);
        if (process is null)
            return true; // already gone

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            return true; // exited in the meantime
        }
        catch (Win32Exception)
        {
            return false;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    public IRunningProcess Start(ProcessStartInfo startInfo, Action<string> onOutput)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(onOutput);

        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        var process = processFactory.Start(startInfo);
        void Forward(object? sender, ProcessLineEventArgs e)
        {
            if (e.Line is not null)
                onOutput(e.Line);
        }

        process.OutputLineReceived += Forward;
        process.ErrorLineReceived += Forward;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return new RunningProcess(process);
    }

    private sealed class RunningProcess(IProcess process) : IRunningProcess
    {
        public int Id { get; } = process.Id;

        public bool HasExited => process.HasExited;

        public int? ExitCode => process.HasExited ? process.ExitCode : null;

        public void Dispose() => process.Dispose();
    }
}
