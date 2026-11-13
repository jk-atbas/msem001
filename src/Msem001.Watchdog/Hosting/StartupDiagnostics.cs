using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;

namespace Msem001.Watchdog.Hosting;

/// <summary>
/// Logs account, elevation and the effective configuration on start.
/// The Jenkins agent inherits the account of the service, so a missing elevation is reported as error.
/// </summary>
internal sealed partial class StartupDiagnostics(
    IOptions<WatchdogOptions> options,
    IEnvironmentProvider environment,
    ILogger<StartupDiagnostics> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var account = $"{environment.UserDomainName}\\{environment.UserName}";
        var elevated = environment.IsPrivilegedProcess;

        var version = typeof(StartupDiagnostics).Assembly.GetName().Version?.ToString() ?? "?";
        LogIdentity(logger, account, elevated, environment.MachineName, version);
        if (!elevated)
            LogNotElevated(logger, account);

        var recipients = string.Join(", ", settings.Mail.Recipients);
        LogConfiguration(
            logger,
            settings.Disk.DriveName,
            settings.Disk.CleanupThresholdGb,
            settings.Disk.PodmanResetThresholdGb,
            settings.Disk.ErrorUsagePercent,
            settings.Disk.CriticalUsagePercent,
            settings.Schedule.NightlyPreCheckTime,
            settings.Podman.MachineName,
            settings.Podman.DockerApiPipeName,
            settings.JenkinsAgent.AgentName,
            settings.JenkinsAgent.Enabled,
            settings.Mail.Enabled,
            recipients);

        LogMemoryConfiguration(
            logger,
            settings.Memory.Enabled,
            settings.Memory.ErrorUsagePercent,
            settings.Memory.CriticalUsagePercent,
            settings.Memory.CheckInterval,
            settings.Memory.SustainedDuration);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        LogStopping(logger);
        return Task.CompletedTask;
    }

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "MSEM001 Watchdog {Version} startet auf {MachineName} als {Account} (Administrator/elevated: {Elevated})")]
    private static partial void LogIdentity(ILogger logger, string account, bool elevated, string machineName, string version);

    [LoggerMessage(EventId = 101, Level = LogLevel.Error, Message = "Der Dienst läuft als {Account} ohne Administratorrechte. Der Jenkins-Agent erbt diese Rechte, Tests mit Admin-Voraussetzung schlagen fehl")]
    private static partial void LogNotElevated(ILogger logger, string account);

    [LoggerMessage(EventId = 102, Level = LogLevel.Information, Message = "Konfiguration: Laufwerk {Drive}, Bereinigung < {CleanupThresholdGb} GB, Podman-Reset < {ResetThresholdGb} GB, Error > {ErrorPercent} %, Critical > {CriticalPercent} %, Nightly-Vorabprüfung {NightlyTime}, Podman-Machine {MachineName}, Pipe {PipeName}, Agent {AgentName} (verwaltet: {AgentEnabled}), Mail aktiv: {MailEnabled}, Empfänger: {Recipients}")]
    private static partial void LogConfiguration(
        ILogger logger,
        string drive,
        double cleanupThresholdGb,
        double resetThresholdGb,
        double errorPercent,
        double criticalPercent,
        TimeOnly nightlyTime,
        string machineName,
        string pipeName,
        string agentName,
        bool agentEnabled,
        bool mailEnabled,
        string recipients);

    [LoggerMessage(EventId = 104, Level = LogLevel.Information, Message = "RAM-Überwachung aktiv: {Enabled}, Error > {ErrorPercent} %, Critical > {CriticalPercent} %, Intervall {Interval}, Meldung nach {SustainedDuration} am Stück")]
    private static partial void LogMemoryConfiguration(ILogger logger, bool enabled, double errorPercent, double criticalPercent, TimeSpan interval, TimeSpan sustainedDuration);

    [LoggerMessage(EventId = 103, Level = LogLevel.Information, Message = "MSEM001 Watchdog wird beendet, ein laufender Jenkins-Agent bleibt verbunden")]
    private static partial void LogStopping(ILogger logger);
}
