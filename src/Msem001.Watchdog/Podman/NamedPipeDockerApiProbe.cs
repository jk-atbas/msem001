using System.IO.Pipes;
using System.Net;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;

namespace Msem001.Watchdog.Podman;

/// <summary>
/// Sends <c>GET /_ping</c> to the Docker API through the named pipe exposed by win-sshproxy
/// (default <c>\\.\pipe\docker_engine</c>). This is exactly the path Docker clients in the builds use.
/// </summary>
internal sealed class NamedPipeDockerApiProbe(IOptions<WatchdogOptions> options) : IDockerApiProbe
{
    private static readonly Uri PingUri = new("http://docker/_ping");

    public async Task<DockerApiProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value.Podman;
        var pipeName = settings.DockerApiPipeName;

        using var handler = new SocketsHttpHandler
        {
            ConnectTimeout = settings.ProbeTimeout,
            PooledConnectionLifetime = TimeSpan.Zero,
            UseProxy = false,
            ConnectCallback = async (_, token) =>
            {
                var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await pipe.ConnectAsync(token).ConfigureAwait(false);
                    return pipe;
                }
                catch
                {
                    await pipe.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            },
        };
        using var client = new HttpClient(handler) { Timeout = settings.ProbeTimeout };

        try
        {
            using var response = await client.GetAsync(PingUri, cancellationToken).ConfigureAwait(false);
            var body = (await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Trim();

            return response.StatusCode == HttpStatusCode.OK && string.Equals(body, "OK", StringComparison.OrdinalIgnoreCase)
                ? new DockerApiProbeResult(true, $@"\\.\pipe\{pipeName}: /_ping OK")
                : new DockerApiProbeResult(false, $@"\\.\pipe\{pipeName}: /_ping lieferte HTTP {(int)response.StatusCode} '{Truncate(body)}'");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TimeoutException or UnauthorizedAccessException
                                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return new DockerApiProbeResult(false, $@"\\.\pipe\{pipeName}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string Truncate(string value) => value.Length <= 200 ? value : value[..200];
}
