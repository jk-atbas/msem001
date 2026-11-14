using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Podman;

namespace Msem001.Watchdog.Maintenance;

/// <summary>
/// Removes running containers older than <see cref="ContainerCleanupOptions.MaxRunTime"/>.
/// Builds start containers only for their own duration (e.g. Testcontainers), long running ones are leftovers.
/// </summary>
internal sealed partial class ContainerCleanup(
    IPodmanContainers containers,
    MaintenanceExecutor executor,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<ContainerCleanup> logger)
{
    /// <summary>Checks the running containers once and removes the long running ones.</summary>
    /// <returns>Number of removed containers.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // A maintenance may reset the Podman machine right now; the next cycle checks again.
        if (executor.IsBusy)
        {
            LogSkippedBusy(logger);
            return 0;
        }

        var running = await containers.ListRunningAsync(cancellationToken).ConfigureAwait(false);
        if (running is null)
            return 0;

        var settings = options.Value.Containers;
        var now = timeProvider.GetUtcNow();
        var removed = 0;
        foreach (var container in running)
        {
            var runTime = TruncateToSeconds(now - container.StartedAt);
            if (runTime <= settings.MaxRunTime)
                continue;

            if (IsExcluded(container, settings.ExcludedNamePrefixes))
            {
                LogExcluded(logger, container.Name, runTime);
                continue;
            }

            if (await containers.RemoveAsync(container, cancellationToken).ConfigureAwait(false))
            {
                removed++;
                LogRemoved(logger, container.Name, container.Image, runTime, settings.MaxRunTime);
            }
        }

        LogChecked(logger, running.Count, removed);
        return removed;
    }

    private static bool IsExcluded(PodmanContainer container, IEnumerable<string> prefixes) =>
        prefixes.Any(prefix => !string.IsNullOrWhiteSpace(prefix) && container.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static TimeSpan TruncateToSeconds(TimeSpan value) => new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond));

    [LoggerMessage(EventId = 14000, Level = LogLevel.Debug, Message = "Container-Prüfung übersprungen, eine Wartung läuft")]
    private static partial void LogSkippedBusy(ILogger logger);

    [LoggerMessage(EventId = 14001, Level = LogLevel.Warning, Message = "Container {ContainerName} ({Image}) lief seit {RunTime} (erlaubt {MaxRunTime}) und wurde entfernt")]
    private static partial void LogRemoved(ILogger logger, string containerName, string image, TimeSpan runTime, TimeSpan maxRunTime);

    [LoggerMessage(EventId = 14002, Level = LogLevel.Debug, Message = "Container {ContainerName} läuft seit {RunTime}, ist aber ausgenommen")]
    private static partial void LogExcluded(ILogger logger, string containerName, TimeSpan runTime);

    [LoggerMessage(EventId = 14003, Level = LogLevel.Debug, Message = "Container-Prüfung: {Running} laufende Container, {Removed} entfernt")]
    private static partial void LogChecked(ILogger logger, int running, int removed);
}
