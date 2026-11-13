using System.ComponentModel;
using System.Diagnostics;
using Msem001.Watchdog.Processes;
using Msem001.Watchdog.Processes.Abstractions;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Processes;

/// <summary>
/// Unit tests of <see cref="SystemProcesses"/> against mocked process abstractions.
/// </summary>
public sealed class SystemProcessesTests
{
    private readonly Mock<IProcessFactory> factory = Mock.Of<IProcessFactory>();
    private readonly Mock<IProcessQuery> query = Mock.Of<IProcessQuery>();
    private readonly Mock<IProcess> process = Mock.Of<IProcess>();
    private readonly SystemProcesses processes;

    public SystemProcessesTests()
    {
        processes = new SystemProcesses(factory.Object, query.Object);
    }

    [Test]
    public async Task FindByImageName_DelegatesToQuery()
    {
        query.FindByImageName("java.exe").Returns([new ProcessSnapshot(1, "java.exe", "java -jar agent.jar")]);

        var found = processes.FindByImageName("java.exe");

        await Assert.That(found).HasSingleItem();
        await Assert.That(found[0].CommandLine).IsEqualTo("java -jar agent.jar");
    }

    [Test]
    public async Task CountByName_CountsAndDisposesProcesses()
    {
        var other = Mock.Of<IProcess>();
        factory.GetProcessesByName("win-sshproxy").Returns([process.Object, other.Object]);

        await Assert.That(processes.CountByName("win-sshproxy")).IsEqualTo(2);

        process.Dispose().WasCalled(Times.Once);
        other.Dispose().WasCalled(Times.Once);
    }

    [Test]
    public async Task KillTreeAsync_KillsWholeTreeAndWaits()
    {
        factory.GetProcessById(42).Returns(process.Object);

        await Assert.That(await processes.KillTreeAsync(42, TimeSpan.FromSeconds(5), TestData.Ct)).IsTrue();

        process.Kill(true).WasCalled(Times.Once);
        process.WaitForExitAsync(Arg.Any<CancellationToken>()).WasCalled(Times.Once);
        process.Dispose().WasCalled(Times.Once);
    }

    [Test]
    public async Task KillTreeAsync_ProcessGone_ReturnsTrue()
    {
        await Assert.That(await processes.KillTreeAsync(42, TimeSpan.FromSeconds(5), TestData.Ct)).IsTrue();
    }

    [Test]
    public async Task KillTreeAsync_ExitedInTheMeantime_ReturnsTrue()
    {
        factory.GetProcessById(42).Returns(process.Object);
        process.Kill(Arg.Any<bool>()).Throws(new InvalidOperationException("exited"));

        await Assert.That(await processes.KillTreeAsync(42, TimeSpan.FromSeconds(5), TestData.Ct)).IsTrue();
    }

    [Test]
    public async Task KillTreeAsync_AccessDenied_ReturnsFalse()
    {
        factory.GetProcessById(42).Returns(process.Object);
        process.Kill(Arg.Any<bool>()).Throws(new Win32Exception(5));

        await Assert.That(await processes.KillTreeAsync(42, TimeSpan.FromSeconds(5), TestData.Ct)).IsFalse();
    }

    [Test]
    public async Task KillTreeAsync_DoesNotExitInTime_ReturnsFalse()
    {
        factory.GetProcessById(42).Returns(process.Object);

        // Like the real Process: never exits, but honours the timeout token passed by the caller.
        process.WaitForExitAsync(Arg.Any<CancellationToken>()).Returns(token => Task.Delay(Timeout.Infinite, token));

        await Assert.That(await processes.KillTreeAsync(42, TimeSpan.FromMilliseconds(50), TestData.Ct)).IsFalse();
    }

    [Test]
    public async Task Start_ForcesRedirectionAndForwardsBothStreams()
    {
        process.Id.Returns(4711);
        factory.Start(Arg.Any<ProcessStartInfo>()).Returns(process.Object);
        process.BeginErrorReadLine().Callback(() =>
        {
            process.RaiseOutputLineReceived(new ProcessLineEventArgs("out"));
            process.RaiseErrorLineReceived(new ProcessLineEventArgs("err"));
            process.RaiseOutputLineReceived(new ProcessLineEventArgs(null));
        });
        var lines = new List<string>();

        using var started = processes.Start(new ProcessStartInfo("java") { UseShellExecute = true }, lines.Add);

        await Assert.That(started.Id).IsEqualTo(4711);
        await Assert.That(started.ExitCode).IsNull();
        await Assert.That(string.Join("|", lines)).IsEqualTo("out|err");
        factory.Start(Arg.Is<ProcessStartInfo>(p => !p.UseShellExecute && p.RedirectStandardOutput && p.RedirectStandardError && p.CreateNoWindow))
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task Start_ExitedProcess_ReportsExitCode()
    {
        process.HasExited.Returns(true);
        process.ExitCode.Returns(1);
        factory.Start(Arg.Any<ProcessStartInfo>()).Returns(process.Object);

        using var started = processes.Start(new ProcessStartInfo("java"), _ => { });

        await Assert.That(started.HasExited).IsTrue();
        await Assert.That(started.ExitCode).IsEqualTo(1);
    }
}
