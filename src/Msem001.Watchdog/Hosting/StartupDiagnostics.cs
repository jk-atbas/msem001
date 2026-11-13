using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;

namespace Msem001.Watchdog.Hosting;

/// <summary>
/// Logs account, administrator rights and the effective configuration on start.
/// The Jenkins agent inherits the token of the service, so missing administrator rights are reported as error.
/// </summary>
internal sealed partial class StartupDiagnostics(
    IOptions<WatchdogOptions> options,
    IEnvironmentProvider environment,
    IProcessTokenInspector tokenInspector,
    ILogger<StartupDiagnostics> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var account = $"{environment.UserDomainName}\\{environment.UserName}";
        var version = typeof(StartupDiagnostics).Assembly.GetName().Version?.ToString() ?? "?";
        CheckAdministratorRights(account, version);

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

        var agent = settings.JenkinsAgent;
        if (agent.Enabled && agent.IsApiConfigured)
            LogGracefulMaintenance(logger, agent.ApiUser, agent.DrainTimeout, settings.Podman.RepairDrainTimeout, settings.Disk.ImmediateMaintenanceThresholdGb);
        else if (agent.Enabled)
            LogGracefulMaintenanceDisabled(logger);

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

    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "MSEM001 Watchdog {Version} startet auf {MachineName} als {Account} (Administratorrechte: {Elevated})")]
    private static partial void LogIdentity(ILogger logger, string account, bool elevated, string machineName, string version);

    [LoggerMessage(EventId = 106, Level = LogLevel.Information, Message = "Prozess-Token: Elevation {ElevationType}, Administratorgruppe {AdministratorsGroup}, Integritätsstufe {IntegrityLevel}, TypeOfAdminApprovalMode {AdminApprovalMode}")]
    private static partial void LogToken(ILogger logger, TokenElevationKind elevationType, AdministratorsGroupState administratorsGroup, string integrityLevel, int? adminApprovalMode);

    [LoggerMessage(EventId = 101, Level = LogLevel.Error, Message = "Der Dienst läuft als {Account} ohne Administratorrechte. Der Jenkins-Agent erbt diese Rechte, Tests mit Admin-Voraussetzung schlagen fehl. Ursache: {Diagnosis}")]
    private static partial void LogNotElevated(ILogger logger, string account, string diagnosis);

    [LoggerMessage(EventId = 105, Level = LogLevel.Error, Message = "Der Dienst läuft als {Account} ohne Administratorrechte. Der Jenkins-Agent erbt diese Rechte, Tests mit Admin-Voraussetzung schlagen fehl. Das Token konnte nicht untersucht werden")]
    private static partial void LogNotElevatedUnknownCause(ILogger logger, string account, Exception exception);

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

    [LoggerMessage(EventId = 107, Level = LogLevel.Information, Message = "Sanfte Wartung aktiv (Jenkins-API als {ApiUser}): Agent wird vor einer Wartung offline geschaltet, laufende Builds werden höchstens {DrainTimeout} abgewartet (Podman-Reparatur {RepairDrainTimeout}), unter {ImmediateGb} GB freiem Speicher sofort")]
    private static partial void LogGracefulMaintenance(ILogger logger, string apiUser, TimeSpan drainTimeout, TimeSpan repairDrainTimeout, double immediateGb);

    [LoggerMessage(EventId = 108, Level = LogLevel.Warning, Message = "Sanfte Wartung inaktiv (Watchdog:JenkinsAgent:ApiUser/ApiToken nicht gesetzt): eine Wartung bricht laufende Builds sofort ab")]
    private static partial void LogGracefulMaintenanceDisabled(ILogger logger);

    [LoggerMessage(EventId = 103, Level = LogLevel.Information, Message = "MSEM001 Watchdog wird beendet, ein laufender Jenkins-Agent bleibt verbunden")]
    private static partial void LogStopping(ILogger logger);

    private void CheckAdministratorRights(string account, string version)
    {
        ProcessTokenInfo token;
        try
        {
            token = tokenInspector.Inspect();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.Security.SecurityException or UnauthorizedAccessException)
        {
            // Fallback: TokenIsElevated is stricter than necessary (false for services without a split token), but better than nothing.
            var elevated = environment.IsPrivilegedProcess;
            LogIdentity(logger, account, elevated, environment.MachineName, version);
            if (!elevated)
                LogNotElevatedUnknownCause(logger, account, ex);
            return;
        }

        var hasRights = token.HasAdministratorRights;
        LogIdentity(logger, account, hasRights, environment.MachineName, version);
        LogToken(logger, token.ElevationType, token.AdministratorsGroup, token.IntegrityLevelName, token.AdminApprovalMode);
        if (!hasRights)
            LogNotElevated(logger, account, token.Explain());
    }
}
