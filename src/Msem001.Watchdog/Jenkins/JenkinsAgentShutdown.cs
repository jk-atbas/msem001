using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Maintenance;

namespace Msem001.Watchdog.Jenkins;

/// <summary>
/// Terminates the Jenkins agent when the service stops (<see cref="JenkinsAgentOptions.StopWithService"/>).
/// <see cref="StoppingAsync"/> runs before any hosted service stops and blocks further agent starts;
/// <see cref="StopAsync"/> runs after all workers have stopped (registered before them) and terminates the agent.
/// </summary>
internal sealed partial class JenkinsAgentShutdown(
    IJenkinsAgent agent,
    MaintenanceExecutor executor,
    IOptions<WatchdogOptions> options,
    ILogger<JenkinsAgentShutdown> logger) : IHostedLifecycleService
{
    private bool IsActive => options.Value.JenkinsAgent is { Enabled: true, StopWithService: true };

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        if (IsActive)
        {
            executor.SuppressAgentRestart();
            LogStopping(logger, options.Value.JenkinsAgent.AgentName);
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.JenkinsAgent;
        if (!IsActive)
        {
            if (settings.Enabled)
                LogKeepsRunning(logger, settings.AgentName);
            return;
        }

        try
        {
            // Not cancellable: killing is immediate, the wait for the exit is limited by JenkinsAgent:StopTimeout.
            await agent.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStopFailed(logger, settings.AgentName, ex);
        }
    }

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(EventId = 5200, Level = LogLevel.Information, Message = "Dienst wird beendet: Jenkins-Agent {AgentName} wird mit beendet, laufende Builds werden abgebrochen")]
    private static partial void LogStopping(ILogger logger, string agentName);

    [LoggerMessage(EventId = 5201, Level = LogLevel.Information, Message = "Dienst wird beendet, Jenkins-Agent {AgentName} bleibt verbunden (Watchdog:JenkinsAgent:StopWithService = false)")]
    private static partial void LogKeepsRunning(ILogger logger, string agentName);

    [LoggerMessage(EventId = 5202, Level = LogLevel.Error, Message = "Jenkins-Agent {AgentName} konnte beim Beenden des Dienstes nicht beendet werden")]
    private static partial void LogStopFailed(ILogger logger, string agentName, Exception exception);
}
