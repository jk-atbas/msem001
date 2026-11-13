using System.Diagnostics;

namespace Msem001.Watchdog.Processes.Abstractions;

#pragma warning disable RS0030 // This adapter is the only place that may use System.Diagnostics.Process directly.

/// <summary>
/// <see cref="IProcessFactory"/> backed by <see cref="Process"/>.
/// </summary>
internal sealed class ProcessFactory : IProcessFactory
{
    public IProcess Start(ProcessStartInfo startInfo)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var wrapper = new ProcessWrapper(process);
        try
        {
            process.Start();
            return wrapper;
        }
        catch
        {
            wrapper.Dispose();
            throw;
        }
    }

    public IProcess? GetProcessById(int processId)
    {
        try
        {
            return new ProcessWrapper(Process.GetProcessById(processId));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public IReadOnlyList<IProcess> GetProcessesByName(string processName) =>
        [.. Process.GetProcessesByName(processName).Select(p => new ProcessWrapper(p))];

    private sealed class ProcessWrapper : IProcess
    {
        private readonly Process process;

        public ProcessWrapper(Process process)
        {
            this.process = process;
            process.OutputDataReceived += (_, e) => OutputLineReceived?.Invoke(this, new ProcessLineEventArgs(e.Data));
            process.ErrorDataReceived += (_, e) => ErrorLineReceived?.Invoke(this, new ProcessLineEventArgs(e.Data));
            process.Exited += (_, _) => Exited?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler<ProcessLineEventArgs>? OutputLineReceived;

        public event EventHandler<ProcessLineEventArgs>? ErrorLineReceived;

        public event EventHandler? Exited;

        public int Id => process.Id;

        public bool HasExited => process.HasExited;

        public int ExitCode => process.ExitCode;

        public void BeginOutputReadLine() => process.BeginOutputReadLine();

        public void BeginErrorReadLine() => process.BeginErrorReadLine();

        public void Kill(bool entireProcessTree) => process.Kill(entireProcessTree);

        public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);

        public void Dispose() => process.Dispose();
    }
}
