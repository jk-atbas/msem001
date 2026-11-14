using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Processes;

namespace Msem001.Watchdog.Podman;

/// <summary>
/// <see cref="IPodmanContainers"/> based on the Podman CLI (<c>podman ps</c> / <c>podman rm</c>).
/// </summary>
internal sealed partial class PodmanContainerCli(
    IProcessRunner processRunner,
    IOptions<WatchdogOptions> options,
    ILogger<PodmanContainerCli> logger) : IPodmanContainers
{
    public async Task<IReadOnlyList<PodmanContainer>?> ListRunningAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Podman;
        var request = new ProcessRunRequest(settings.Executable, ["ps", "--format", "json"])
        {
            DisplayName = "podman ps",
            Timeout = settings.CommandTimeout,
            OutputLogLevel = LogLevel.Trace,
        };

        var result = await processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            LogListFailed(logger, result.ExitCode, result.TimedOut, result.StandardErrorText);
            return null;
        }

        try
        {
            return ParseContainers(result.StandardOutputText);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            LogListUnreadable(logger, ex);
            return null;
        }
    }

    public async Task<bool> RemoveAsync(PodmanContainer container, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(container);

        var podman = options.Value.Podman;
        var stopSeconds = ((int)Math.Ceiling(options.Value.Containers.StopTimeout.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        var request = new ProcessRunRequest(podman.Executable, ["rm", "--force", "--volumes", "--time", stopSeconds, container.Id])
        {
            DisplayName = "podman rm",
            Timeout = podman.CommandTimeout,
            OutputLogLevel = LogLevel.Debug,
        };

        var result = await processRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Succeeded || IsNoSuchContainerError(result.StandardErrorText))
            return true;

        LogRemoveFailed(logger, container.Name, result.ExitCode, result.TimedOut, result.StandardErrorText);
        return false;
    }

    /// <summary>Parses the JSON array of <c>podman ps --format json</c>; pod infra containers are skipped.</summary>
    internal static IReadOnlyList<PodmanContainer> ParseContainers(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new JsonException("podman ps did not return a JSON array.");

        var containers = new List<PodmanContainer>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.TryGetProperty("IsInfra", out var infra) && infra.ValueKind == JsonValueKind.True)
                continue;

            var id = element.GetProperty("Id").GetString() ?? throw new JsonException("Container without Id.");
            var name = element.TryGetProperty("Names", out var names) && names.ValueKind == JsonValueKind.Array && names.GetArrayLength() > 0
                ? names[0].GetString() ?? id
                : id;
            var image = element.TryGetProperty("Image", out var imageValue) && imageValue.ValueKind == JsonValueKind.String ? imageValue.GetString()! : string.Empty;
            containers.Add(new PodmanContainer(id, name, image, ReadStartedAt(element)));
        }

        return containers;
    }

    internal static bool IsNoSuchContainerError(string standardError) =>
        standardError.Contains("no such container", StringComparison.OrdinalIgnoreCase);

    // StartedAt is 0 or negative if unknown; Created is the fallback.
    private static DateTimeOffset ReadStartedAt(JsonElement element)
    {
        var started = element.TryGetProperty("StartedAt", out var startedAt) && startedAt.ValueKind == JsonValueKind.Number ? startedAt.GetInt64() : 0;
        if (started <= 0)
            started = element.GetProperty("Created").GetInt64();

        return DateTimeOffset.FromUnixTimeSeconds(started);
    }

    [LoggerMessage(EventId = 4300, Level = LogLevel.Warning, Message = "podman ps fehlgeschlagen (Exit-Code {ExitCode}, Timeout {TimedOut}), laufende Container nicht geprüft: {StandardError}")]
    private static partial void LogListFailed(ILogger logger, int exitCode, bool timedOut, string standardError);

    [LoggerMessage(EventId = 4301, Level = LogLevel.Warning, Message = "Ausgabe von podman ps nicht lesbar, laufende Container nicht geprüft")]
    private static partial void LogListUnreadable(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4302, Level = LogLevel.Warning, Message = "Container {ContainerName} konnte nicht entfernt werden (Exit-Code {ExitCode}, Timeout {TimedOut}): {StandardError}")]
    private static partial void LogRemoveFailed(ILogger logger, string containerName, int exitCode, bool timedOut, string standardError);
}
