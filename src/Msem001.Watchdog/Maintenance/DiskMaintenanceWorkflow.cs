using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Disk;
using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Scripts;

namespace Msem001.Watchdog.Maintenance;

/// <summary>
/// Free space below <see cref="DiskOptions.CleanupThresholdGb"/>: clean the Jenkins workspace.
/// Still below <see cref="DiskOptions.PodmanResetThresholdGb"/> afterwards: reset the Podman machine.
/// The Jenkins agent is stopped before and reconnected after the routine. Running builds may finish first,
/// unless the free space drops below <see cref="DiskOptions.ImmediateMaintenanceThresholdGb"/>.
/// </summary>
internal sealed partial class DiskMaintenanceWorkflow(
    IDiskSpaceProbe diskSpaceProbe,
    IMaintenanceScripts scripts,
    PodmanRecovery podmanRecovery,
    MaintenanceExecutor executor,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<DiskMaintenanceWorkflow> logger)
{
    private readonly Lock sync = new();
    private DateTimeOffset? lastMaintenanceAt;

    public async Task<DiskMaintenanceOutcome> RunAsync(MaintenanceTrigger trigger, CancellationToken cancellationToken)
    {
        var settings = options.Value.Disk;
        var before = diskSpaceProbe.GetDiskSpace();

        if (before.FreeGigabytes >= settings.CleanupThresholdGb)
        {
            if (trigger == MaintenanceTrigger.Nightly)
                LogSufficientSpaceNightly(logger, before, settings.CleanupThresholdGb);
            else
                LogSufficientSpace(logger, before, settings.CleanupThresholdGb);

            return DiskMaintenanceOutcome.NotRequired;
        }

        if (trigger == MaintenanceTrigger.Periodic && executor.IsBusy)
        {
            LogBusy(logger, before);
            return DiskMaintenanceOutcome.SkippedBusy;
        }

        var now = timeProvider.GetUtcNow();
        lock (sync)
        {
            if (trigger == MaintenanceTrigger.Periodic && lastMaintenanceAt is { } last && now - last < settings.MaintenanceCooldown)
            {
                LogCooldownActive(logger, before, last, settings.MaintenanceCooldown);
                return DiskMaintenanceOutcome.SkippedCooldown;
            }

            lastMaintenanceAt = now;
        }

        LogLowSpace(logger, trigger, before, settings.CleanupThresholdGb);
        return await executor
            .RunWithAgentStoppedAsync("Speicherbereinigung", token => RunMaintenanceAsync(before, token), IsCriticallyLow, cancellationToken)
            .ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 7000, Level = LogLevel.Debug, Message = "Ausreichend Speicherplatz: {Disk} (Schwelle {ThresholdGb} GB)")]
    private static partial void LogSufficientSpace(ILogger logger, DiskSpaceInfo disk, double thresholdGb);

    [LoggerMessage(EventId = 7001, Level = LogLevel.Information, Message = "Nightly-Vorabprüfung: ausreichend Speicherplatz: {Disk} (Schwelle {ThresholdGb} GB)")]
    private static partial void LogSufficientSpaceNightly(ILogger logger, DiskSpaceInfo disk, double thresholdGb);

    [LoggerMessage(EventId = 7002, Level = LogLevel.Warning, Message = "Wenig Speicherplatz, letzte Wartung um {LastMaintenanceAt} liegt innerhalb der Sperrfrist {Cooldown}: {Disk}")]
    private static partial void LogCooldownActive(ILogger logger, DiskSpaceInfo disk, DateTimeOffset lastMaintenanceAt, TimeSpan cooldown);

    [LoggerMessage(EventId = 7003, Level = LogLevel.Warning, Message = "Wenig Speicherplatz ({Trigger}): {Disk} (Schwelle {ThresholdGb} GB), starte Bereinigung")]
    private static partial void LogLowSpace(ILogger logger, MaintenanceTrigger trigger, DiskSpaceInfo disk, double thresholdGb);

    [LoggerMessage(EventId = 7004, Level = LogLevel.Error, Message = "Bereinigung des Jenkins-Workspaces konnte nicht ausgeführt werden")]
    private static partial void LogCleanupCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 7005, Level = LogLevel.Information, Message = "Workspace-Bereinigung hat {FreedGb:F1} GB freigegeben (vorher {FreeBeforeGb:F1} GB frei), jetzt: {Disk}")]
    private static partial void LogCleanupResult(ILogger logger, double freedGb, double freeBeforeGb, DiskSpaceInfo disk);

    [LoggerMessage(EventId = 7006, Level = LogLevel.Information, Message = "Podman-Reset hat {FreedGb:F1} GB freigegeben, jetzt: {Disk}")]
    private static partial void LogResetResult(ILogger logger, double freedGb, DiskSpaceInfo disk);

    [LoggerMessage(EventId = 7007, Level = LogLevel.Error, Message = "Trotz Wartung weiterhin wenig Speicherplatz: {Disk} (Schwelle {ThresholdGb} GB), manuelles Eingreifen erforderlich")]
    private static partial void LogStillLowSpace(ILogger logger, DiskSpaceInfo disk, double thresholdGb);

    [LoggerMessage(EventId = 7008, Level = LogLevel.Information, Message = "Speicherwartung abgeschlossen ({Outcome}): frei vorher {FreeBeforeGb:F1} GB, nach Bereinigung {FreeAfterCleanupGb:F1} GB, final {FreeFinalGb:F1} GB")]
    private static partial void LogSummary(ILogger logger, DiskMaintenanceOutcome outcome, double freeBeforeGb, double freeAfterCleanupGb, double freeFinalGb);

    [LoggerMessage(EventId = 7009, Level = LogLevel.Debug, Message = "Wenig Speicherplatz, eine Wartungsroutine läuft bereits: {Disk}")]
    private static partial void LogBusy(ILogger logger, DiskSpaceInfo disk);

    [LoggerMessage(EventId = 7010, Level = LogLevel.Warning, Message = "Kritisch wenig Speicherplatz: {Disk} (< {ThresholdGb} GB), laufende Builds werden nicht abgewartet")]
    private static partial void LogCriticallyLow(ILogger logger, DiskSpaceInfo disk, double thresholdGb);

    [LoggerMessage(EventId = 7011, Level = LogLevel.Warning, Message = "Speicherplatz konnte während des Wartens nicht gemessen werden")]
    private static partial void LogProbeFailed(ILogger logger, Exception exception);

    private bool IsCriticallyLow()
    {
        var threshold = options.Value.Disk.ImmediateMaintenanceThresholdGb;
        try
        {
            var disk = diskSpaceProbe.GetDiskSpace();
            if (disk.FreeGigabytes >= threshold)
                return false;

            LogCriticallyLow(logger, disk, threshold);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogProbeFailed(logger, ex);
            return false;
        }
    }

    private async Task<DiskMaintenanceOutcome> RunMaintenanceAsync(DiskSpaceInfo before, CancellationToken cancellationToken)
    {
        var settings = options.Value.Disk;

        try
        {
            await scripts.RunJenkinsWorkspaceCleanupAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogCleanupCrashed(logger, ex);
        }

        var afterCleanup = diskSpaceProbe.GetDiskSpace();
        LogCleanupResult(logger, afterCleanup.FreeGigabytes - before.FreeGigabytes, before.FreeGigabytes, afterCleanup);

        var outcome = DiskMaintenanceOutcome.CleanupOnly;
        var final = afterCleanup;
        if (afterCleanup.FreeGigabytes < settings.PodmanResetThresholdGb)
        {
            await podmanRecovery
                .ResetAsync($"Nach der Workspace-Bereinigung nur {afterCleanup.FreeGigabytes:F1} GB frei (< {settings.PodmanResetThresholdGb} GB)", cancellationToken)
                .ConfigureAwait(false);

            final = diskSpaceProbe.GetDiskSpace();
            LogResetResult(logger, final.FreeGigabytes - afterCleanup.FreeGigabytes, final);
            outcome = DiskMaintenanceOutcome.CleanupAndPodmanReset;
        }
        else
        {
            // Podman was not touched, but make sure the machine runs before the agent reconnects.
            await podmanRecovery.EnsureHealthyAsync(allowReset: false, cancellationToken).ConfigureAwait(false);
        }

        if (final.FreeGigabytes < settings.CleanupThresholdGb)
            LogStillLowSpace(logger, final, settings.CleanupThresholdGb);

        LogSummary(logger, outcome, before.FreeGigabytes, afterCleanup.FreeGigabytes, final.FreeGigabytes);
        return outcome;
    }
}
