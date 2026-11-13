using System.IO.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Alerting;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Hosting;
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

        var hostedServices = provider.GetServices<IHostedService>().Select(s => s.GetType().Name);
        await Assert.That(string.Join(",", hostedServices)).IsEqualTo(
            $"{nameof(StartupDiagnostics)},{nameof(MailAlertDispatcher)},{nameof(BuildAgentHealthWorker)},{nameof(DiskMonitorWorker)},{nameof(NightlyPreCheckWorker)}");
        await Assert.That(provider.GetServices<ILoggerProvider>()).Contains(p => p is MailAlertLoggerProvider);

        // Real system abstractions in production
        await Assert.That(provider.GetRequiredService<IFileSystem>()).IsTypeOf<FileSystem>();
        await Assert.That(provider.GetRequiredService<IEnvironmentProvider>()).IsSameReferenceAs(SystemEnvironmentProvider.Instance);
        await Assert.That(provider.GetRequiredService<IProcessFactory>()).IsTypeOf<ProcessFactory>();
        await Assert.That(provider.GetRequiredService<IProcessQuery>()).IsTypeOf<WmiProcessQuery>();
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
        environment.IsPrivilegedProcess.Returns(true);
        var diagnostics = new StartupDiagnostics(TestData.CreateOptions(), environment.Object, logger);

        await diagnostics.StartAsync(TestData.Ct);
        await diagnostics.StopAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage(@"auf MSEM001 als MSEM001\atbas (Administrator/elevated: True)").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("Bereinigung < 50 GB").WasCalled(Times.Once);
        logger.VerifyLog().AtLevel(LogLevel.Information).ContainingMessage("beendet").WasCalled(Times.Once);
        logger.VerifyNoLog(LogLevel.Error);
    }

    [Test]
    public async Task StartupDiagnostics_NotElevated_LogsError()
    {
        var logger = Mock.Logger<StartupDiagnostics>();
        var environment = Mock.Of<IEnvironmentProvider>();
        environment.UserDomainName.Returns("MSEM001");
        environment.UserName.Returns("atbas");
        environment.IsPrivilegedProcess.Returns(false);

        await new StartupDiagnostics(TestData.CreateOptions(), environment.Object, logger).StartAsync(TestData.Ct);

        logger.VerifyLog().AtLevel(LogLevel.Error).ContainingMessage("ohne Administratorrechte").WasCalled(Times.Once);
    }
}
