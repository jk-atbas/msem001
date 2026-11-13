using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Processes;
using Msem001.Watchdog.Processes.Abstractions;
using Msem001.Watchdog.Scripts;
using Msem001.Watchdog.Tests.TestSupport;
using Testably.Abstractions.Testing;

namespace Msem001.Watchdog.Tests.Scripts;

public sealed class PowerShellMaintenanceScriptsTests
{
    private const string ScriptsDirectory = TestData.ServiceDirectory + @"\scripts";

    private readonly MockFileSystem fileSystem = TestData.CreateFileSystem();
    private readonly Mock<IProcessRunner> runner = Mock.Of<IProcessRunner>();
    private readonly MockLogger<PowerShellMaintenanceScripts> logger = Mock.Logger<PowerShellMaintenanceScripts>();

    public PowerShellMaintenanceScriptsTests()
    {
        fileSystem.Directory.CreateDirectory(ScriptsDirectory);
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Success());
    }

    [Test]
    [Arguments("scripts", "Clean.ps1", @"C:\svc\scripts\Clean.ps1")]
    [Arguments(@"D:\tools", "Clean.ps1", @"D:\tools\Clean.ps1")]
    [Arguments("scripts", @"E:\x\Clean.ps1", @"E:\x\Clean.ps1")]
    [Arguments("", "Clean.ps1", @"C:\svc\Clean.ps1")]
    [Arguments(@"..\shared", "Clean.ps1", @"C:\shared\Clean.ps1")]
    public async Task ResolveScriptPath_ResolvesAgainstServiceDirectory(string directory, string file, string expected)
    {
        await Assert.That(PowerShellMaintenanceScripts.ResolveScriptPath(fileSystem.Path, directory, file, @"C:\svc")).IsEqualTo(expected);
    }

    [Test]
    public async Task BuildArguments_RunsScriptNonInteractive()
    {
        var arguments = PowerShellMaintenanceScripts.BuildArguments(@"C:\s\Reset.ps1", ["-Force"]);

        await Assert.That(string.Join(' ', arguments)).IsEqualTo(@"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File C:\s\Reset.ps1 -Force");
    }

    [Test]
    [Arguments("", "-Force")]
    [Arguments("-force", "-Force")]
    [Arguments("-Cpus 6", "-Force -Cpus 6")]
    public async Task BuildPodmanResetArguments_AlwaysStartsWithSingleForce(string additional, string expected)
    {
        var additionalArguments = additional.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        await Assert.That(string.Join(' ', PowerShellMaintenanceScripts.BuildPodmanResetArguments(additionalArguments))).IsEqualTo(expected);
    }

    [Test]
    public async Task RunPodmanResetAsync_RunsScriptFromServiceDirectoryWithForce()
    {
        fileSystem.File.WriteAllText(ScriptsDirectory + @"\Reset-PodmanMachine.ps1", "exit 0");

        var result = await CreateSut().RunPodmanResetAsync(TestData.Ct);

        await Assert.That(result.Succeeded).IsTrue();
        runner.RunAsync(
            Arg.Is<ProcessRunRequest>(r =>
                r.FileName == "pwsh.exe"
                && string.Join(' ', r.Arguments) == $@"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File {ScriptsDirectory}\Reset-PodmanMachine.ps1 -Force"
                && r.Timeout == TimeSpan.FromMinutes(30)
                && r.WorkingDirectory == ScriptsDirectory
                && r.DisplayName == "Reset-PodmanMachine.ps1"
                && r.OutputLogLevel == LogLevel.Information),
            Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("erfolgreich").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunJenkinsWorkspaceCleanupAsync_PassesConfiguredArguments()
    {
        fileSystem.File.WriteAllText(ScriptsDirectory + @"\Clean-JenkinsWorkspace.ps1", "exit 0");

        await CreateSut(o => o.Scripts.CleanupArguments = ["-Verbose"]).RunJenkinsWorkspaceCleanupAsync(TestData.Ct);

        runner.RunAsync(Arg.Is<ProcessRunRequest>(r => r.Arguments[^1] == "-Verbose" && r.Timeout == TimeSpan.FromMinutes(30)), Arg.Any<CancellationToken>())
            .WasCalled(Times.Once);
    }

    [Test]
    public async Task RunJenkinsWorkspaceCleanupAsync_Failure_IsLoggedAsErrorWithStandardError()
    {
        fileSystem.File.WriteAllText(ScriptsDirectory + @"\Clean-JenkinsWorkspace.ps1", "exit 1");
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(TestData.Failure(1, "Zugriff verweigert"));

        var result = await CreateSut().RunJenkinsWorkspaceCleanupAsync(TestData.Ct);

        await Assert.That(result.Succeeded).IsFalse();
        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("Zugriff verweigert").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunJenkinsWorkspaceCleanupAsync_Timeout_IsLoggedAsError()
    {
        fileSystem.File.WriteAllText(ScriptsDirectory + @"\Clean-JenkinsWorkspace.ps1", "exit 1");
        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).Returns(new ProcessRunResult(-1, true, [], [], TimeSpan.FromMinutes(30)));

        await CreateSut().RunJenkinsWorkspaceCleanupAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("Timeout").WasCalled(Times.Once);
    }

    [Test]
    public async Task RunScript_MissingFile_ThrowsWithoutStartingPowerShell()
    {
        await Assert.That(async () => { await CreateSut().RunJenkinsWorkspaceCleanupAsync(TestData.Ct); })
            .Throws<FileNotFoundException>()
            .WithMessageContaining(ScriptsDirectory);

        runner.RunAsync(Arg.Any<ProcessRunRequest>(), Arg.Any<CancellationToken>()).WasNeverCalled();
    }

    [Test]
    [Category("Integration")]
    public async Task RunScript_WithRealPowerShell_ReturnsOutputAndExitCode()
    {
        var realFileSystem = new System.IO.Abstractions.FileSystem();
        var directory = realFileSystem.Directory.CreateTempSubdirectory("msem001-tests-").FullName;
        try
        {
            realFileSystem.File.WriteAllText(realFileSystem.Path.Combine(directory, "Clean-JenkinsWorkspace.ps1"), "param([string]$Name) Write-Output \"Hallo $Name\"; exit 7");
            var sut = new PowerShellMaintenanceScripts(
                new ProcessRunner(new ProcessFactory(), TimeProvider.System, Mock.Logger<ProcessRunner>()),
                realFileSystem,
                TestData.CreateHostEnvironment(directory),
                TestData.CreateOptions(o =>
                {
                    o.Scripts.ScriptsDirectory = string.Empty;
                    o.Scripts.CleanupArguments = ["-Name", "MSEM001"];
                }),
                logger);

            var result = await sut.RunJenkinsWorkspaceCleanupAsync(TestData.Ct);

            await Assert.That(result.ExitCode).IsEqualTo(7);
            await Assert.That(result.StandardOutput).Contains("Hallo MSEM001");
        }
        finally
        {
            realFileSystem.Directory.Delete(directory, recursive: true);
        }
    }

    private PowerShellMaintenanceScripts CreateSut(Action<WatchdogOptions>? configure = null) =>
        new(runner.Object, fileSystem, TestData.CreateHostEnvironment(), TestData.CreateOptions(configure), logger);
}
