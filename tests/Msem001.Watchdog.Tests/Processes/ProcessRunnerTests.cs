using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Msem001.Watchdog.Processes;
using Msem001.Watchdog.Processes.Abstractions;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Processes;

/// <summary>
/// Unit tests of <see cref="ProcessRunner"/> against a mocked <see cref="IProcessFactory"/>.
/// </summary>
public sealed class ProcessRunnerTests
{
    private readonly Mock<IProcessFactory> factory = Mock.Of<IProcessFactory>();
    private readonly Mock<IProcess> process = Mock.Of<IProcess>();
    private readonly MockLogger<ProcessRunner> logger = Mock.Logger<ProcessRunner>();
    private readonly FakeTimeProvider time = new();
    private readonly ProcessRunner runner;

    public ProcessRunnerTests()
    {
        factory.Start(Arg.Any<ProcessStartInfo>()).Returns(process.Object);
        runner = new ProcessRunner(factory.Object, time, logger);
    }

    [Test]
    public async Task RunAsync_CapturesOutputAndExitCode()
    {
        process.ExitCode.Returns(3);
        process.BeginErrorReadLine().Callback(() =>
        {
            process.RaiseOutputLineReceived(new ProcessLineEventArgs("hello"));
            process.RaiseOutputLineReceived(new ProcessLineEventArgs(null));
            process.RaiseErrorLineReceived(new ProcessLineEventArgs("oops"));
            process.RaiseErrorLineReceived(new ProcessLineEventArgs(null));
            process.RaiseExited(EventArgs.Empty);
        });

        var result = await runner.RunAsync(new ProcessRunRequest("tool.exe", ["a"]) { OutputLogLevel = LogLevel.Information }, TestData.Ct);

        await Assert.That(result.ExitCode).IsEqualTo(3);
        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.StandardOutput).IsEquivalentTo(["hello"]);
        await Assert.That(result.StandardErrorText).IsEqualTo("oops");
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("[tool.exe] hello").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("oops").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_PassesFileArgumentsAndWorkingDirectory()
    {
        process.BeginErrorReadLine().Callback(CloseStreams);
        process.HasExited.Returns(true);
        var request = new ProcessRunRequest("podman", ["machine", "inspect", "--format", "{{.State}}"]) { WorkingDirectory = @"C:\work" };

        await runner.RunAsync(request, TestData.Ct);

        factory.Start(Arg.Is<ProcessStartInfo>(p =>
            p.FileName == "podman"
            && string.Join(' ', p.ArgumentList) == "machine inspect --format {{.State}}"
            && p.WorkingDirectory == @"C:\work"
            && p.RedirectStandardOutput && p.RedirectStandardError && !p.UseShellExecute && p.CreateNoWindow))
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_ProcessExitedBeforeSubscription_DoesNotHang()
    {
        process.BeginErrorReadLine().Callback(CloseStreams);
        process.HasExited.Returns(true);

        var result = await runner.RunAsync(new ProcessRunRequest("tool.exe", []), TestData.Ct);

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task RunAsync_DoesNotWaitForEndOfStreamsHeldOpenByGrandchildren()
    {
        // win-sshproxy inherits the redirected handles after "podman machine start": the process exits, EOF never comes.
        process.BeginErrorReadLine().Callback(() => process.RaiseExited(EventArgs.Empty));

        var run = runner.RunAsync(new ProcessRunRequest("podman", ["machine", "start"]), TestData.Ct);
        await Wait.UntilAsync(() => run.IsCompleted, time, ProcessRunner.OutputDrainTimeout);

        await Assert.That((await run).Succeeded).IsTrue();
    }

    [Test]
    public async Task RunAsync_Timeout_KillsProcessTree()
    {
        process.BeginErrorReadLine().Callback(CloseStreams);
        process.Kill(Arg.Any<bool>()).Callback(() => process.RaiseExited(EventArgs.Empty));
        var request = new ProcessRunRequest("ping.exe", []) { Timeout = TimeSpan.FromMilliseconds(50) };

        var result = await runner.RunAsync(request, TestData.Ct);

        await Assert.That(result.TimedOut).IsTrue();
        await Assert.That(result.ExitCode).IsEqualTo(-1);
        process.Kill(true).WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("Timeout").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_KillFails_IsLoggedAndRunStillCompletes()
    {
        process.BeginErrorReadLine().Callback(CloseStreams);
        process.Kill(Arg.Any<bool>()).Throws(new Win32Exception(5, "Zugriff verweigert"));

        var result = await runner.RunAsync(new ProcessRunRequest("x.exe", []) { Timeout = TimeSpan.FromMilliseconds(50) }, TestData.Ct);

        await Assert.That(result.TimedOut).IsTrue();
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("nicht sauber beendet").WithException<Win32Exception>().WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_Cancellation_KillsAndThrows()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.That(async () => { await runner.RunAsync(new ProcessRunRequest("x.exe", []), cancellation.Token); }).Throws<OperationCanceledException>();

        process.Kill(true).WasCalled(Times.Once);
    }

    [Test]
    public async Task RunAsync_StartFails_ThrowsInvalidOperationWithFileName()
    {
        factory.Start(Arg.Any<ProcessStartInfo>()).Throws(new Win32Exception(2, "Das System kann die Datei nicht finden"));

        await Assert.That(async () => { await runner.RunAsync(new ProcessRunRequest("does-not-exist.exe", []), TestData.Ct); })
            .Throws<InvalidOperationException>()
            .WithMessageContaining("does-not-exist.exe");
    }

    [Test]
    public async Task RunAsync_DisposesProcess()
    {
        process.BeginErrorReadLine().Callback(CloseStreams);
        process.HasExited.Returns(true);

        await runner.RunAsync(new ProcessRunRequest("x.exe", []), TestData.Ct);

        process.Dispose().WasCalled(Times.Once);
    }

    /// <summary>A real process closes stdout/stderr when it ends; without this the runner waits for the drain timeout on the fake clock.</summary>
    private void CloseStreams()
    {
        process.RaiseOutputLineReceived(new ProcessLineEventArgs(null));
        process.RaiseErrorLineReceived(new ProcessLineEventArgs(null));
    }
}
