using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Podman;

namespace Msem001.Watchdog.Maintenance;

/// <summary>
/// Removes containers left over by builds: running ones older than <see cref="ContainerCleanupOptions.MaxRunTime"/>
/// and not running ones (created, exited …) older than <see cref="ContainerCleanupOptions.MaxStoppedTime"/>.
/// Builds use containers only for their own duration (e.g. Testcontainers), everything older is a leftover.
/// </summary>
internal sealed partial class ContainerCleanup(
    IPodmanContainers containers,
    MaintenanceExecutor executor,
    IOptions<WatchdogOptions> options,
    TimeProvider timeProvider,
    ILogger<ContainerCleanup> logger)
{
    /// <summary>Checks the containers once and removes the left over ones.</summary>
    /// <returns>Number of removed containers.</returns>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        // A maintenance may reset the Podman machine right now; the next cycle checks again.
        if (executor.IsBusy)
        {
            LogSkippedBusy(logger);
            return 0;
        }

        var all = await containers.ListAsync(cancellationToken).ConfigureAwait(false);
        if (all is null)
            return 0;

        var settings = options.Value.Containers;
        var now = timeProvider.GetUtcNow();
        var removed = 0;
        foreach (var container in all)
        {
            if (container.IsRemoving || (!container.IsRunning && !settings.RemoveStopped))
                continue;

            var age = TruncateToSeconds(now - container.StateSince);
            var maxAge = container.IsRunning ? settings.MaxRunTime : settings.MaxStoppedTime;
            if (age <= maxAge)
                continue;

            if (IsExcluded(container, settings.ExcludedNamePrefixes))
            {
                LogExcluded(logger, container.Name, container.State, age);
                continue;
            }

            if (!await containers.RemoveAsync(container, cancellationToken).ConfigureAwait(false))
                continue;

            removed++;
            if (container.IsRunning)
                LogRemovedRunning(logger, container.Name, container.Image, age, maxAge);
            else
                LogRemovedStopped(logger, container.Name, container.Image, container.State, age, maxAge);
        }

        LogChecked(logger, all.Count, removed);
        return removed;
    }

    private static bool IsExcluded(PodmanContainer container, IEnumerable<string> prefixes) =>
        prefixes.Any(prefix => !string.IsNullOrWhiteSpace(prefix) && container.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static TimeSpan TruncateToSeconds(TimeSpan value) => new(value.Ticks - (value.Ticks % TimeSpan.TicksPerSecond));

    [LoggerMessage(EventId = 14000, Level = LogLevel.Debug, Message = "Container-Prüfung übersprungen, eine Wartung läuft")]
    private static partial void LogSkippedBusy(ILogger logger);

    [LoggerMessage(EventId = 14001, Level = LogLevel.Warning, Message = "Container {ContainerName} ({Image}) lief seit {Age} (erlaubt {MaxAge}) und wurde entfernt")]
    private static partial void LogRemovedRunning(ILogger logger, string containerName, string image, TimeSpan age, TimeSpan maxAge);

    [LoggerMessage(EventId = 14004, Level = LogLevel.Warning, Message = "Liegengebliebener Container {ContainerName} ({Image}, Zustand {State} seit {Age}, erlaubt {MaxAge}) wurde entfernt")]
    private static partial void LogRemovedStopped(ILogger logger, string containerName, string image, string state, TimeSpan age, TimeSpan maxAge);

    [LoggerMessage(EventId = 14002, Level = LogLevel.Debug, Message = "Container {ContainerName} ({State} seit {Age}) ist ausgenommen")]
    private static partial void LogExcluded(ILogger logger, string containerName, string state, TimeSpan age);

    [LoggerMessage(EventId = 14003, Level = LogLevel.Debug, Message = "Container-Prüfung: {Count} Container, {Removed} entfernt")]
    private static partial void LogChecked(ILogger logger, int count, int removed);
}
