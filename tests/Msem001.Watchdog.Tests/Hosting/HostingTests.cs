using System.IO.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Alerting;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Hosting;
using Msem001.Watchdog.Jenkins;
using Msem001.Watchdog.Memory;
using Msem001.Watchdog.Processes;
using Msem001.Watchdog.Processes.Abstractions;
using Msem001.Watchdog.Tests.TestSupport;
using Msem001.Watchdog.Workers;

namespace Msem001.Watchdog.Tests.Hosting;

public sealed class HostingTests
{
    private static readonly string AppSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    [Test]
    public async Task AddWatchdog_ResolvesAllServicesAndBindsConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(AppSettingsPath, optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Watchdog:JenkinsAgent:Secret"] = "geheim",
                ["Watchdog:Mail:Recipients:0"] = "devops",
                ["Watchdog:Schedule:NightlyPreCheckTime"] = "20:30",
            })
            .Build();

        // Registered by Host.CreateApplicationBuilder in production.
        var services = new ServiceCollection().AddLogging().AddSingleton<IHostEnvironment>(TestData.CreateHostEnvironment());

        using var provider = services
            .AddWatchdog(configuration)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var options = provider.GetRequiredService<IOptions<WatchdogOptions>>().Value;
        await Assert.That(options.JenkinsAgent.Secret).IsEqualTo("geheim");
        await Assert.That(options.Schedule.NightlyPreCheckTime).IsEqualTo(new TimeOnly(20, 30));
        await Assert.That(options.Disk.CleanupThresholdGb).IsEqualTo(50);
        await Assert.That(options.Disk.PodmanResetThresholdGb).IsEqualTo(200);
        await Assert.That(options.Scripts.PodmanResetArguments).IsEmpty();
        await Assert.That(options.Memory.ErrorUsagePercent).IsEqualTo(90);
        await Assert.That(options.Memory.CriticalUsagePercent).IsEqualTo(95);
        await Assert.That(options.Memory.SustainedDuration).IsEqualTo(TimeSpan.FromMinutes(5));

        var hostedServices = provider.GetServices<IHostedService>().Select(s => s.GetType().Name);
        await Assert.That(string.Join(",", hostedServices)).IsEqualTo(
            $"{nameof(StartupDiagnostics)},{nameof(MailAlertDispatcher)},{nameof(BuildAgentHealthWorker)},{nameof(DiskMonitorWorker)},{nameof(MemoryMonitorWorker)},{nameof(NightlyPreCheckWorker)}");
        await Assert.That(provider.GetServices<ILoggerProvider>()).Contains(p => p is MailAlertLoggerProvider);

        // Real system abstractions in production
        await Assert.That(provider.GetRequiredService<IFileSystem>()).IsTypeOf<FileSystem>();
        await Assert.That(provider.GetRequiredService<IEnvironmentProvider>()).IsSameReferenceAs(SystemEnvironmentProvider.Instance);
        await Assert.That(provider.GetRequiredService<IProcessFactory>()).IsTypeOf<ProcessFactory>();
        await Assert.That(provider.GetRequiredService<IProcessQuery>()).IsTypeOf<WmiProcessQuery>();
        await Assert.That(provider.GetRequiredService<IMemoryProbe>()).IsTypeOf<WindowsMemoryProbe>();
        await Assert.That(provider.GetRequiredService<IProcessTokenInspector>()).IsTypeOf<WindowsProcessTokenInspector>();
        await Assert.That(provider.GetRequiredService<IJenkinsApi>()).IsTypeOf<JenkinsApiClient>();
        await Assert.That(provider.GetRequiredService<IJenkinsAgentDrain>()).IsTypeOf<JenkinsAgentDrain>();
        await Assert.That(options.Disk.ImmediateMaintenanceThresholdGb).IsEqualTo(10);
        await Assert.That(options.JenkinsAgent.DrainTimeout).IsEqualTo(TimeSpan.FromHours(1));
        await Assert.That(options.Podman.RepairDrainTimeout).IsEqualTo(TimeSpan.FromMinutes(10));
        await Assert.That(options.JenkinsAgent.IsApiConfigured).IsFalse();
    }

    [Test]
    public async Task AddWatchdog_KeepsPreRegisteredSystemAbstractions()
    {
        var fileSystem = TestData.CreateFileSystem();
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IFileSystem>(fileSystem);
        services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider());

        using var provider = services.AddWatchdog(new ConfigurationBuilder().Build()).BuildServiceProvider();

        await Assert.That(provider.GetRequiredService<IFileSystem>()).IsSameReferenceAs(fileSystem);
        await Assert.That(provider.GetRequiredService<TimeProvider>()).IsTypeOf<Microsoft.Extensions.Time.Testing.FakeTimeProvider>();
    }

    [Test]
    public async Task AddWatchdog_MissingSecret_FailsValidation()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(AppSettingsPath, optional: false).Build();
        using var provider = new ServiceCollection().AddLogging().AddWatchdog(configuration).BuildServiceProvider();

        await Assert.That(() => provider.GetRequiredService<IOptions<WatchdogOptions>>().Value)
            .Throws<OptionsValidationException>()
            .WithMessageContaining("Secret");
    }

    [Test]
    public async Task ScriptsAreDeployedNextToTheService()
    {
        await Assert.That(File.Exists(Path.Combine(AppContext.BaseDirectory, "scripts", "Clean-JenkinsWorkspace.ps1"))).IsTrue();
        await Assert.That(File.Exists(Path.Combine(AppContext.BaseDirectory, "scripts", "Reset-PodmanMachine.ps1"))).IsTrue();
    }

    [Test]
    public async Task StartupDiagnostics_LogsIdentityAndConfiguration()
    {
        var logger = Mock.Logger<StartupDiagnostics>();
        var environment = Mock.Of<IEnvironmentProvider>();
        environment.MachineName.Returns("MSEM001");
        environment.UserDomainName.Returns("MSEM001");
        environment.UserName.Returns("atbas");
        var tokenInspector = Inspector(new ProcessTokenInfo(TokenElevationKind.Full, AdministratorsGroupState.Enabled, 1, ProcessTokenInfo.HighIntegrity));
        var diagnostics = new StartupDiagnostics(TestData.CreateOptions(), environment.Object, tokenInspector, logger);

        await diagnostics.StartAsync(TestData.Ct);
        await diagnostics.StopAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage(@"auf MSEM001 als MSEM001\atbas (Administratorrechte: True)").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("Elevation Full, Administratorgruppe Enabled, Integritätsstufe High").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("Bereinigung < 50 GB").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("RAM-Überwachung aktiv: True, Error > 90 %, Critical > 95 %").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Warning).ContainingMessage("Sanfte Wartung inaktiv").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("beendet").WasCalled(Times.Once);
        logger.VerifyNoLog(LogLevel.Error);
    }

    [Test]
    [Arguments(true, true, "Sanfte Wartung aktiv (Jenkins-API als watchdog): Agent wird vor einer Wartung offline geschaltet, laufende Builds werden höchstens 01:00:00 abgewartet (Podman-Reparatur 00:10:00)")]
    [Arguments(true, false, "Sanfte Wartung inaktiv")]
    [Arguments(false, false, null)]
    public async Task StartupDiagnostics_LogsGracefulMaintenanceState(bool agentEnabled, bool apiConfigured, string? expected)
    {
        var logger = Mock.Logger<StartupDiagnostics>();
        var options = TestData.CreateOptions(o =>
        {
            o.JenkinsAgent.Enabled = agentEnabled;
            if (apiConfigured)
            {
                o.JenkinsAgent.ApiUser = "watchdog";
                o.JenkinsAgent.ApiToken = "t0ken";
            }
        });
        var tokenInspector = Inspector(new ProcessTokenInfo(TokenElevationKind.Full, AdministratorsGroupState.Enabled, 1, ProcessTokenInfo.HighIntegrity));

        await new StartupDiagnostics(options, NotElevatedEnvironment(), tokenInspector, logger).StartAsync(TestData.Ct);

        if (expected is null)
            logger.VerifyLog().ContainingMessage("Sanfte Wartung").WasNeverCalled();
        else
            logger.VerifyLog().ContainingMessage(expected).WasCalled(Times.Once);
    }

    [Test]
    public async Task StartupDiagnostics_ServiceTokenWithoutElevationFlag_IsAdministrator()
    {
        // Situation on MSEM001: service logon, no UAC split token, TokenIsElevated = false.
        var logger = Mock.Logger<StartupDiagnostics>();
        var tokenInspector = Inspector(new ProcessTokenInfo(TokenElevationKind.Default, AdministratorsGroupState.Enabled, 1, ProcessTokenInfo.HighIntegrity));

        await new StartupDiagnostics(TestData.CreateOptions(), NotElevatedEnvironment(), tokenInspector, logger).StartAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("(Administratorrechte: True)").WasCalled(Times.Once);
        logger.VerifyNoLog(LogLevel.Error);
    }

    [Test]
    public async Task StartupDiagnostics_NoAdministratorRights_LogsErrorWithDiagnosis()
    {
        var logger = Mock.Logger<StartupDiagnostics>();
        var tokenInspector = Inspector(new ProcessTokenInfo(TokenElevationKind.Default, AdministratorsGroupState.Missing, ProcessTokenInfo.AdministratorProtectionMode, 0x2000));

        await new StartupDiagnostics(TestData.CreateOptions(), NotElevatedEnvironment(), tokenInspector, logger).StartAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("Elevation Default, Administratorgruppe Missing, Integritätsstufe Medium, TypeOfAdminApprovalMode 2").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("ohne Administratorrechte").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("Windows-Administratorschutz ist aktiv").WasCalled(Times.Once);
    }

    [Test]
    [Arguments(false, 1)]
    [Arguments(true, 0)]
    public async Task StartupDiagnostics_InspectionFails_FallsBackToEnvironment(bool privileged, int expectedErrors)
    {
        var logger = Mock.Logger<StartupDiagnostics>();
        var tokenInspector = Mock.Of<IProcessTokenInspector>();
        tokenInspector.Inspect().Throws(new UnauthorizedAccessException("Registry"));
        var environment = Mock.Of<IEnvironmentProvider>();
        environment.UserDomainName.Returns("MSEM001");
        environment.UserName.Returns("atbas");
        environment.IsPrivilegedProcess.Returns(privileged);

        await new StartupDiagnostics(TestData.CreateOptions(), environment.Object, tokenInspector.Object, logger).StartAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage($"(Administratorrechte: {privileged})").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("konnte nicht untersucht werden").WithException<UnauthorizedAccessException>().WasCalled(Times.Exactly(expectedErrors));
    }

    private static IProcessTokenInspector Inspector(ProcessTokenInfo info)
    {
        var inspector = Mock.Of<IProcessTokenInspector>();
        inspector.Inspect().Returns(info);
        return inspector.Object;
    }

    private static IEnvironmentProvider NotElevatedEnvironment()
    {
        var environment = Mock.Of<IEnvironmentProvider>();
        environment.UserDomainName.Returns("MSEM001");
        environment.UserName.Returns("atbas");
        environment.IsPrivilegedProcess.Returns(false);
        return environment.Object;
    }
}
