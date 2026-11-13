using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Jenkins;
using Msem001.Watchdog.Processes;
using Msem001.Watchdog.Tests.TestSupport;
using Testably.Abstractions.Testing;

namespace Msem001.Watchdog.Tests.Jenkins;

public sealed class JenkinsAgentControllerTests
{
    private const string AgentJar = @"C:\jenkins\agent.jar";
    private const string AgentCommandLine =
        "\"C:\\Program Files\\Java\\bin\\java.exe\" -jar agent.jar -url http://10.24.9.62:8080/ -secret x -name \"einarbeitungs-laptop\" -webSocket -workDir \"C:\\jenkins\"";

    private readonly MockFileSystem fileSystem = TestData.CreateFileSystem();
    private readonly Mock<ISystemProcesses> processes = Mock.Of<ISystemProcesses>();
    private readonly Mock<IRunningProcess> running = Mock.Of<IRunningProcess>();
    private readonly MockLogger<JenkinsAgentController> logger = Mock.Logger<JenkinsAgentController>();

    public JenkinsAgentControllerTests()
    {
        fileSystem.Directory.CreateDirectory(@"C:\jenkins");
        fileSystem.File.WriteAllText(AgentJar, "jar");
        processes.FindByImageName(Arg.Any<string>()).Returns([]);
        processes.Start(Arg.Any<ProcessStartInfo>(), Arg.Any<Action<string>>()).Returns(running.Object);
    }

    [Test]
    [Arguments(AgentCommandLine, true)]
    [Arguments(@"java -jar C:\jenkins\AGENT.JAR -name EINARBEITUNGS-LAPTOP", true)]
    [Arguments("java -jar agent.jar -name other-node", false)]
    [Arguments("java -jar jenkins.war", false)]
    [Arguments("", false)]
    [Arguments(null, false)]
    public async Task IsAgentCommandLine_MatchesJarAndNodeName(string? commandLine, bool expected)
    {
        await Assert.That(JenkinsAgentController.IsAgentCommandLine(commandLine, new JenkinsAgentOptions(), fileSystem.Path)).IsEqualTo(expected);
    }

    [Test]
    public async Task BuildArguments_MatchesDocumentedCommand()
    {
        var arguments = JenkinsAgentController.BuildArguments(new JenkinsAgentOptions { Secret = "geheim" });

        await Assert.That(string.Join(' ', arguments)).IsEqualTo(
            @"-jar C:\jenkins\agent.jar -url http://10.24.9.62:8080/ -secret geheim -name einarbeitungs-laptop -webSocket -workDir C:\jenkins");
    }

    [Test]
    public async Task BuildArguments_WithoutWebSocket_OmitsFlag()
    {
        await Assert.That(JenkinsAgentController.BuildArguments(new JenkinsAgentOptions { UseWebSocket = false })).DoesNotContain("-webSocket");
    }

    [Test]
    public async Task CreateStartInfo_UsesJarDirectoryAsWorkingDirectory()
    {
        var startInfo = JenkinsAgentController.CreateStartInfo(new JenkinsAgentOptions { AgentJarPath = @"D:\agents\agent.jar" }, fileSystem.Path);

        await Assert.That(startInfo.FileName).IsEqualTo("java");
        await Assert.That(startInfo.WorkingDirectory).IsEqualTo(@"D:\agents");
        await Assert.That(startInfo.ArgumentList).Contains("-secret");
    }

    [Test]
    public async Task FormatArguments_MasksSecretAndQuotesSpaces()
    {
        await Assert.That(JenkinsAgentController.FormatArguments(["-secret", "geheim", "-workDir", @"C:\my dir"], "geheim"))
            .IsEqualTo(@"-secret *** -workDir ""C:\my dir""");
    }

    [Test]
    public async Task IsRunning_DetectsJavawAgents()
    {
        processes.FindByImageName("javaw.exe").Returns([new ProcessSnapshot(42, "javaw.exe", AgentCommandLine)]);

        await Assert.That(CreateSut().IsRunning()).IsTrue();
    }

    [Test]
    public async Task StopAsync_KillsAllAgentProcessesButNoOtherJava()
    {
        processes.FindByImageName("java.exe").Returns(
        [
            new ProcessSnapshot(10, "java.exe", AgentCommandLine),
            new ProcessSnapshot(11, "java.exe", "java -jar gradle-daemon.jar"),
        ]);
        processes.KillTreeAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);

        await CreateSut().StopAsync(TestData.Ct);

        processes.KillTreeAsync(10, TimeSpan.FromSeconds(30), Arg.Any<CancellationToken>()).WasCalled(Times.Once);
        processes.KillTreeAsync(11, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).WasNeverCalled();
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("(PID 10) beendet").WasCalled(Times.Once);
    }

    [Test]
    public async Task StopAsync_KillFails_LogsError()
    {
        processes.FindByImageName("java.exe").Returns([new ProcessSnapshot(10, "java.exe", AgentCommandLine)]);
        processes.KillTreeAsync(Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(false);

        await CreateSut().StopAsync(TestData.Ct);

        await Assert.That(logger.Latest().LogLevel).IsEqualTo(LogLevel.Error);
    }

    [Test]
    public async Task StopAsync_NotRunning_LogsInformation()
    {
        await CreateSut().StopAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("läuft nicht").WasCalled(Times.Once);
    }

    [Test]
    public async Task StopAsync_Disabled_DoesNothing()
    {
        await CreateSut(o => o.JenkinsAgent.Enabled = false).StopAsync(TestData.Ct);

        processes.FindByImageName(Arg.Any<string>()).WasNeverCalled();
    }

    [Test]
    public async Task StartAsync_StartsAgentWithSecretButLogsItMasked()
    {
        running.Id.Returns(4711);

        await CreateSut().StartAsync(TestData.Ct);

        processes.Start(
            Arg.Is<ProcessStartInfo>(p => p.ArgumentList.Contains("s3cr3t") && p.ArgumentList.Contains(AgentJar) && p.WorkingDirectory == @"C:\jenkins"),
            Arg.Any<Action<string>>())
            .WasCalled(Times.Once);
        await Assert.That(logger.Entries).DoesNotContain(e => e.Message.Contains("s3cr3t", StringComparison.Ordinal));
        await Assert.That(logger.Latest().Message).Contains("4711");
    }

    [Test]
    public async Task StartAsync_AgentExitsImmediately_LogsError()
    {
        running.HasExited.Returns(true);
        running.ExitCode.Returns(1);

        await CreateSut().StartAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("Exit-Code 1").WasCalled(Times.Once);
    }

    [Test]
    public async Task StartAsync_AlreadyRunning_DoesNotStartSecondAgent()
    {
        processes.FindByImageName("java.exe").Returns([new ProcessSnapshot(10, "java.exe", AgentCommandLine)]);

        await CreateSut().StartAsync(TestData.Ct);

        processes.Start(Arg.Any<ProcessStartInfo>(), Arg.Any<Action<string>>()).WasNeverCalled();
    }

    [Test]
    public async Task StartAsync_JarMissing_LogsError()
    {
        fileSystem.File.Delete(AgentJar);

        await CreateSut().StartAsync(TestData.Ct);

        processes.Start(Arg.Any<ProcessStartInfo>(), Arg.Any<Action<string>>()).WasNeverCalled();
        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("agent.jar fehlt").WasCalled(Times.Once);
    }

    [Test]
    public async Task StartAsync_JavaMissing_LogsError()
    {
        processes.Start(Arg.Any<ProcessStartInfo>(), Arg.Any<Action<string>>()).Throws(new Win32Exception(2));

        await CreateSut().StartAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Error).WithException<Win32Exception>().WasCalled(Times.Once);
    }

    [Test]
    public async Task StartAsync_ForwardsAgentOutputToSeparateLogCategory()
    {
        processes.Start(Arg.Any<ProcessStartInfo>(), Arg.Any<Action<string>>()).Returns((_, onOutput) =>
        {
            onOutput("INFO: Connected");
            return running.Object;
        });
        var outputLogger = Mock.Logger(JenkinsAgentController.OutputLoggerCategory);
        var loggerFactory = Mock.Of<ILoggerFactory>();
        loggerFactory.CreateLogger(JenkinsAgentController.OutputLoggerCategory).Returns(outputLogger);

        using var agent = new JenkinsAgentController(processes.Object, fileSystem, TestData.CreateOptions(), TimeProvider.System, loggerFactory.Object, logger);
        await agent.StartAsync(TestData.Ct);

        await Assert.That(outputLogger.Latest().Message).IsEqualTo("INFO: Connected");
    }

    [Test]
    public async Task StartAsync_Disabled_DoesNothing()
    {
        await CreateSut(o => o.JenkinsAgent.Enabled = false).StartAsync(TestData.Ct);

        processes.Start(Arg.Any<ProcessStartInfo>(), Arg.Any<Action<string>>()).WasNeverCalled();
    }

    [Test]
    public async Task StopAfterStart_DisposesOwnedProcess()
    {
        using var agent = CreateSut();
        await agent.StartAsync(TestData.Ct);

        await agent.StopAsync(TestData.Ct);

        running.Dispose().WasCalled(Times.Once);
    }

    private JenkinsAgentController CreateSut(Action<WatchdogOptions>? configure = null) =>
        new(
            processes.Object,
            fileSystem,
            TestData.CreateOptions(o =>
            {
                o.JenkinsAgent.AgentJarPath = AgentJar;
                configure?.Invoke(o);
            }),
            TimeProvider.System,
            NullLoggerFactory.Instance,
            logger);
}
