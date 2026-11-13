using System.Collections.Concurrent;
using System.Diagnostics;
using Msem001.Watchdog.Processes;
using Msem001.Watchdog.Processes.Abstractions;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Processes;

/// <summary>
/// Integration tests of the thin adapters around the real operating system
/// (<see cref="ProcessFactory"/>, <see cref="WmiProcessQuery"/>) and of the <see cref="ProcessRunner"/> with real processes.
/// </summary>
[Category("Integration")]
public sealed class ProcessAdapterIntegrationTests
{
    private readonly ProcessFactory factory = new();

    [Test]
    public async Task ProcessRunner_RealProcess_CapturesOutputAndExitCode()
    {
        var runner = new ProcessRunner(factory, TimeProvider.System, Mock.Logger<ProcessRunner>());

        var result = await runner.RunAsync(new ProcessRunRequest("cmd.exe", ["/c", "echo hello& echo oops 1>&2& exit /b 3"]), TestData.Ct);

        await Assert.That(result.ExitCode).IsEqualTo(3);
        await Assert.That(result.StandardOutputText).IsEqualTo("hello");
        await Assert.That(result.StandardErrorText).Contains("oops");
    }

    [Test]
    public async Task ProcessRunner_RealProcess_DoesNotWaitForDetachedGrandchildren()
    {
        var runner = new ProcessRunner(factory, TimeProvider.System, Mock.Logger<ProcessRunner>());

        var result = await runner.RunAsync(new ProcessRunRequest("cmd.exe", ["/c", "start /b ping -n 15 127.0.0.1 >nul & echo done"]), TestData.Ct);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Duration).IsLessThan(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task ProcessRunner_RealProcess_TimeoutKillsProcess()
    {
        var runner = new ProcessRunner(factory, TimeProvider.System, Mock.Logger<ProcessRunner>());

        var result = await runner.RunAsync(new ProcessRunRequest("ping.exe", ["-n", "30", "127.0.0.1"]) { Timeout = TimeSpan.FromMilliseconds(500) }, TestData.Ct);

        await Assert.That(result.TimedOut).IsTrue();
        await Assert.That(result.Duration).IsLessThan(TimeSpan.FromSeconds(20));
    }

    [Test]
    public async Task ProcessFactory_StartKillAndLookup_WorkWithRealProcess()
    {
        var lines = new ConcurrentQueue<string>();
        var processes = new SystemProcesses(factory, new WmiProcessQuery());

        using var started = processes.Start(new ProcessStartInfo("ping.exe") { ArgumentList = { "-n", "60", "127.0.0.1" } }, lines.Enqueue);
        await Wait.UntilAsync(() => !lines.IsEmpty);

        await Assert.That(processes.FindByImageName("PING.EXE")).Contains(p => p.ProcessId == started.Id && p.CommandLine!.Contains("127.0.0.1", StringComparison.Ordinal));
        await Assert.That(processes.CountByName("PING")).IsGreaterThanOrEqualTo(1);
        await Assert.That(await processes.KillTreeAsync(started.Id, TimeSpan.FromSeconds(10), TestData.Ct)).IsTrue();
        await Assert.That(started.HasExited).IsTrue();
        await Assert.That(factory.GetProcessById(started.Id)).IsNull();
    }

    [Test]
    public async Task ProcessFactory_UnknownExecutable_ThrowsWin32Exception()
    {
        await Assert.That(() => factory.Start(new ProcessStartInfo("does-not-exist-4711.exe") { UseShellExecute = false }))
            .Throws<System.ComponentModel.Win32Exception>();
    }

    [Test]
    public async Task WmiProcessQuery_Unknown_ReturnsEmpty()
    {
        await Assert.That(new WmiProcessQuery().FindByImageName("does-not-exist-4711.exe")).IsEmpty();
    }

    [Test]
    [Arguments("java.exe", "java.exe")]
    [Arguments("a'b", "a\\'b")]
    [Arguments(@"a\b", @"a\\b")]
    public async Task EscapeWql_EscapesQuotesAndBackslashes(string value, string expected)
    {
        await Assert.That(WmiProcessQuery.EscapeWql(value)).IsEqualTo(expected);
    }
}
