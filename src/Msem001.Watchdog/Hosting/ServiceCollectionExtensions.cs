using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Alerting;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Disk;
using Msem001.Watchdog.Jenkins;
using Msem001.Watchdog.Maintenance;
using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Processes;
using Msem001.Watchdog.Processes.Abstractions;
using Msem001.Watchdog.Scripts;
using Msem001.Watchdog.Workers;

namespace Msem001.Watchdog.Hosting;

/// <summary>
/// Composition root of the watchdog.
/// </summary>
internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWatchdog(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<WatchdogOptions>()
            .Bind(configuration.GetSection(WatchdogOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WatchdogOptions>, WatchdogOptionsValidator>();

        // System abstractions (replaced by fakes in tests)
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IFileSystem, FileSystem>();
        services.TryAddSingleton<IEnvironmentProvider>(SystemEnvironmentProvider.Instance);
        services.TryAddSingleton<IProcessFactory, ProcessFactory>();
        services.TryAddSingleton<IProcessQuery, WmiProcessQuery>();

        // Infrastructure
        services.AddSingleton<IDiskSpaceProbe, DriveInfoDiskSpaceProbe>();
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ISystemProcesses, SystemProcesses>();
        services.AddSingleton<IMaintenanceScripts, PowerShellMaintenanceScripts>();
        services.AddSingleton<IPodmanMachine, PodmanCli>();
        services.AddSingleton<IDockerApiProbe, NamedPipeDockerApiProbe>();
        services.AddSingleton<IPodmanHealthChecker, PodmanHealthChecker>();
        services.AddSingleton<IJenkinsAgent, JenkinsAgentController>();

        // Workflows
        services.AddSingleton<DiskUsageAlertEvaluator>();
        services.AddSingleton<PodmanRecovery>();
        services.AddSingleton<MaintenanceExecutor>();
        services.AddSingleton<DiskMaintenanceWorkflow>();
        services.AddSingleton<BuildAgentHealthMonitor>();

        // Mail alerting
        services.AddSingleton<AlertQueue>();
        services.AddSingleton<AlertMailComposer>();
        services.AddSingleton<IMailSender, MailKitMailSender>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, MailAlertLoggerProvider>());

        // Hosted services (StartupDiagnostics first, MailAlertDispatcher before the workers so it is stopped last)
        services.AddHostedService<StartupDiagnostics>();
        services.AddHostedService<MailAlertDispatcher>();
        services.AddHostedService<BuildAgentHealthWorker>();
        services.AddHostedService<DiskMonitorWorker>();
        services.AddHostedService<NightlyPreCheckWorker>();

        return services;
    }
}
