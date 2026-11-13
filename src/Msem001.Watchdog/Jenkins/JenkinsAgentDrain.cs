using System.Text.Json;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;

namespace Msem001.Watchdog.Jenkins;

/// <summary>
/// <see cref="IJenkinsAgentDrain"/> via the Jenkins REST API. Without configured API credentials nothing is waited for
/// and the agent is stopped immediately, as before.
/// </summary>
internal sealed partial class JenkinsAgentDrain(
    IJenkinsApi api,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<JenkinsAgentDrain> logger) : IJenkinsAgentDrain
{
    /// <summary>Prefix of the offline reason; identifies an offline mark set by the watchdog.</summary>
    internal const string OfflineMarker = "[MSEM001 Watchdog]";

    private volatile bool staleCheckPending = true;

    public async Task<bool> DrainAsync(string routineName, Func<bool>? isUrgent, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var settings = options.Value.JenkinsAgent;
        if (!api.IsConfigured)
        {
            LogNotConfigured(logger, routineName);
            return false;
        }

        if (IsUrgent(isUrgent))
        {
            LogUrgentNoWait(logger, routineName);
            return false;
        }

        var markedOffline = false;
        try
        {
            var status = await api.GetNodeStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!status.TemporarilyOffline)
            {
                await api.ToggleOfflineAsync($"{OfflineMarker} Wartung '{routineName}': keine neuen Builds, laufende Builds werden abgewartet", cancellationToken).ConfigureAwait(false);
                markedOffline = true;
                LogMarkedOffline(logger, settings.AgentName, routineName);
            }
            else
            {
                // Our own mark (e.g. left over after a crash) is restored afterwards, a mark set by a person is kept.
                markedOffline = IsOwnMark(status);
                LogAlreadyOffline(logger, settings.AgentName, status.OfflineCauseReason ?? string.Empty);
            }

            await WaitUntilIdleAsync(status, routineName, isUrgent, timeout ?? settings.DrainTimeout, settings.DrainPollInterval, cancellationToken).ConfigureAwait(false);
            return markedOffline;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await RestoreAsync(markedOffline, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (IsApiFailure(ex))
        {
            LogDrainApiFailed(logger, routineName, ex);
            return markedOffline;
        }
    }

    public async Task RestoreAsync(bool markedOffline, CancellationToken cancellationToken)
    {
        if (!markedOffline)
            return;

        var agentName = options.Value.JenkinsAgent.AgentName;
        try
        {
            // Only toggle if still offline, otherwise the toggle would mark the node offline again.
            var status = await api.GetNodeStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.TemporarilyOffline)
                await api.ToggleOfflineAsync(null, cancellationToken).ConfigureAwait(false);

            LogBackOnline(logger, agentName);
        }
        catch (Exception ex) when (IsApiFailure(ex) || ex is OperationCanceledException)
        {
            staleCheckPending = true;
            LogRestoreFailed(logger, agentName, ex);
        }
    }

    public async Task ReleaseStaleOfflineAsync(CancellationToken cancellationToken)
    {
        if (!staleCheckPending || !api.IsConfigured)
            return;

        var agentName = options.Value.JenkinsAgent.AgentName;
        try
        {
            var status = await api.GetNodeStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.TemporarilyOffline && IsOwnMark(status))
            {
                await api.ToggleOfflineAsync(null, cancellationToken).ConfigureAwait(false);
                LogStaleReleased(logger, agentName, status.OfflineCauseReason ?? string.Empty);
            }

            staleCheckPending = false;
        }
        catch (Exception ex) when (IsApiFailure(ex))
        {
            LogStaleCheckFailed(logger, agentName, ex);
        }
    }

    [LoggerMessage(EventId = 5100, Level = LogLevel.Information, Message = "Kein Jenkins-API-Token konfiguriert: Wartung '{RoutineName}' beendet den Agent sofort, laufende Builds werden abgebrochen")]
    private static partial void LogNotConfigured(ILogger logger, string routineName);

    [LoggerMessage(EventId = 5101, Level = LogLevel.Warning, Message = "Wartung '{RoutineName}' ist dringend: laufende Builds werden nicht abgewartet, der Agent wird sofort beendet")]
    private static partial void LogUrgentNoWait(ILogger logger, string routineName);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Information, Message = "Agent {AgentName} in Jenkins vorübergehend offline geschaltet (Wartung '{RoutineName}'), es starten keine neuen Builds")]
    private static partial void LogMarkedOffline(ILogger logger, string agentName, string routineName);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Information, Message = "Agent {AgentName} ist in Jenkins bereits offline geschaltet: {Reason}")]
    private static partial void LogAlreadyOffline(ILogger logger, string agentName, string reason);

    [LoggerMessage(EventId = 5104, Level = LogLevel.Information, Message = "Wartung '{RoutineName}' wartet auf laufende Builds (höchstens {Timeout}, Abfrage alle {PollInterval})")]
    private static partial void LogWaiting(ILogger logger, string routineName, TimeSpan timeout, TimeSpan pollInterval);

    [LoggerMessage(EventId = 5105, Level = LogLevel.Information, Message = "Agent ist frei nach {Waited}, Wartung '{RoutineName}' beginnt")]
    private static partial void LogIdle(ILogger logger, string routineName, TimeSpan waited);

    [LoggerMessage(EventId = 5106, Level = LogLevel.Warning, Message = "Nach {Timeout} läuft noch ein Build, der Agent wird für die Wartung '{RoutineName}' trotzdem beendet")]
    private static partial void LogDrainTimeout(ILogger logger, string routineName, TimeSpan timeout);

    [LoggerMessage(EventId = 5107, Level = LogLevel.Warning, Message = "Wartung '{RoutineName}' ist während des Wartens dringend geworden (nach {Waited}), der Agent wird sofort beendet")]
    private static partial void LogUrgentWhileWaiting(ILogger logger, string routineName, TimeSpan waited);

    [LoggerMessage(EventId = 5108, Level = LogLevel.Error, Message = "Jenkins-API nicht nutzbar, Wartung '{RoutineName}' wartet nicht auf laufende Builds")]
    private static partial void LogDrainApiFailed(ILogger logger, string routineName, Exception exception);

    [LoggerMessage(EventId = 5109, Level = LogLevel.Information, Message = "Agent {AgentName} in Jenkins wieder online geschaltet")]
    private static partial void LogBackOnline(ILogger logger, string agentName);

    [LoggerMessage(EventId = 5110, Level = LogLevel.Error, Message = "Agent {AgentName} konnte in Jenkins nicht wieder online geschaltet werden und nimmt keine Builds an. Neuer Versuch bei der nächsten Agent-Überwachung, sonst in Jenkins manuell online schalten")]
    private static partial void LogRestoreFailed(ILogger logger, string agentName, Exception exception);

    [LoggerMessage(EventId = 5111, Level = LogLevel.Warning, Message = "Agent {AgentName} war noch vom Watchdog offline geschaltet ({Reason}) und wurde wieder online geschaltet")]
    private static partial void LogStaleReleased(ILogger logger, string agentName, string reason);

    [LoggerMessage(EventId = 5112, Level = LogLevel.Warning, Message = "Offline-Status von {AgentName} konnte nicht geprüft werden, neuer Versuch bei der nächsten Agent-Überwachung")]
    private static partial void LogStaleCheckFailed(ILogger logger, string agentName, Exception exception);

    private static bool IsOwnMark(JenkinsNodeStatus status) =>
        status.OfflineCauseReason?.Contains(OfflineMarker, StringComparison.Ordinal) == true;

    private static bool IsUrgent(Func<bool>? isUrgent) => isUrgent?.Invoke() == true;

    private static bool IsApiFailure(Exception ex) =>
        ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
           or TimeoutException or TaskCanceledException { InnerException: TimeoutException };

    private async Task WaitUntilIdleAsync(JenkinsNodeStatus status, string routineName, Func<bool>? isUrgent, TimeSpan timeout, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        var waiting = false;
        while (!status.Idle)
        {
            var waited = timeProvider.GetElapsedTime(started);
            if (waited >= timeout)
            {
                LogDrainTimeout(logger, routineName, timeout);
                return;
            }

            if (IsUrgent(isUrgent))
            {
                LogUrgentWhileWaiting(logger, routineName, waited);
                return;
            }

            if (!waiting)
            {
                LogWaiting(logger, routineName, timeout, pollInterval);
                waiting = true;
            }

            await Task.Delay(pollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
            status = await api.GetNodeStatusAsync(cancellationToken).ConfigureAwait(false);
        }

        if (waiting)
        {
            var waited = timeProvider.GetElapsedTime(started);
            LogIdle(logger, routineName, waited);
        }
    }
}
