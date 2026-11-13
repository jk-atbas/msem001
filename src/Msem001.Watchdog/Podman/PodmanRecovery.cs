using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Scripts;

namespace Msem001.Watchdog.Podman;

/// <summary>
/// Brings the Podman machine including the win-sshproxy Docker API forwarding back into a healthy state.
/// </summary>
/// <remarks>
/// Since Podman 6.x win-sshproxy regularly fails to start (or exits immediately) after a reboot.
/// A plain machine start is tried first; the only reliable fix is a full reset via <c>Reset-PodmanMachine.ps1</c>.
/// </remarks>
internal sealed partial class PodmanRecovery(
    IPodmanHealthChecker healthChecker,
    IPodmanMachine machine,
    IMaintenanceScripts scripts,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<PodmanRecovery> logger)
{
    /// <summary>
    /// Starts the machine if it is not running. If the Docker API is still unreachable and
    /// <paramref name="allowReset"/> is set, the machine is reset.
    /// </summary>
    public async Task<PodmanHealth> EnsureHealthyAsync(bool allowReset, CancellationToken cancellationToken)
    {
        var health = await healthChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (health.IsHealthy)
            return health;

        if (health.MachineState != PodmanMachineState.Running)
        {
            LogMachineNotRunning(logger, health.MachineState);
            health = await StartAndCheckAsync(cancellationToken).ConfigureAwait(false);
            if (health.IsHealthy)
            {
                LogRecoveredByStart(logger, health);
                return health;
            }
        }

        if (!allowReset)
        {
            LogUnhealthyWithoutReset(logger, health);
            return health;
        }

        return await ResetAsync("Docker-API-Weiterleitung defekt: " + health, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resets the machine via script and makes sure it is running afterwards.
    /// </summary>
    public async Task<PodmanHealth> ResetAsync(string reason, CancellationToken cancellationToken)
    {
        LogResetting(logger, reason);

        try
        {
            await scripts.RunPodmanResetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogResetScriptCrashed(logger, ex);
        }

        var health = await healthChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (health.MachineState != PodmanMachineState.Running)
        {
            LogMachineNotRunningAfterReset(logger, health.MachineState);
            health = await StartAndCheckAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (!health.IsHealthy)
        {
            // The proxy may need a moment after the restart at the end of the script.
            await Task.Delay(options.Value.Podman.ProxySettleTime, timeProvider, cancellationToken).ConfigureAwait(false);
            health = await healthChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
        }

        if (health.IsHealthy)
            LogResetSucceeded(logger, health);
        else
            LogResetFailed(logger, health);

        return health;
    }

    [LoggerMessage(EventId = 4200, Level = LogLevel.Warning, Message = "Podman-Machine läuft nicht (Zustand {MachineState}), sie wird gestartet")]
    private static partial void LogMachineNotRunning(ILogger logger, PodmanMachineState machineState);

    [LoggerMessage(EventId = 4201, Level = LogLevel.Information, Message = "Podman-Machine durch Start wiederhergestellt: {Health}")]
    private static partial void LogRecoveredByStart(ILogger logger, PodmanHealth health);

    [LoggerMessage(EventId = 4202, Level = LogLevel.Warning, Message = "Podman ist nicht gesund, ein Reset ist in diesem Ablauf nicht vorgesehen: {Health}")]
    private static partial void LogUnhealthyWithoutReset(ILogger logger, PodmanHealth health);

    [LoggerMessage(EventId = 4203, Level = LogLevel.Warning, Message = "Setze Podman-Machine zurück. Grund: {Reason}")]
    private static partial void LogResetting(ILogger logger, string reason);

    [LoggerMessage(EventId = 4204, Level = LogLevel.Error, Message = "Reset-Skript der Podman-Machine konnte nicht ausgeführt werden")]
    private static partial void LogResetScriptCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4205, Level = LogLevel.Warning, Message = "Podman-Machine läuft nach dem Reset nicht (Zustand {MachineState}), sie wird gestartet")]
    private static partial void LogMachineNotRunningAfterReset(ILogger logger, PodmanMachineState machineState);

    [LoggerMessage(EventId = 4206, Level = LogLevel.Information, Message = "Podman-Machine nach Reset gesund: {Health}")]
    private static partial void LogResetSucceeded(ILogger logger, PodmanHealth health);

    [LoggerMessage(EventId = 4207, Level = LogLevel.Critical, Message = "Podman-Machine ist auch nach dem Reset nicht gesund, manuelles Eingreifen erforderlich: {Health}")]
    private static partial void LogResetFailed(ILogger logger, PodmanHealth health);

    private async Task<PodmanHealth> StartAndCheckAsync(CancellationToken cancellationToken)
    {
        await machine.StartAsync(cancellationToken).ConfigureAwait(false);
        await Task.Delay(options.Value.Podman.ProxySettleTime, timeProvider, cancellationToken).ConfigureAwait(false);
        return await healthChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
    }
}
