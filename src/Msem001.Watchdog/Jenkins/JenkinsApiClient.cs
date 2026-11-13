using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;

namespace Msem001.Watchdog.Jenkins;

/// <summary>
/// <see cref="IJenkinsApi"/> via HTTP basic authentication with user and API token.
/// Requests authenticated with an API token do not need a CSRF crumb.
/// Redirects are not followed: <c>toggleOffline</c> answers with a redirect to the node page, and
/// <see cref="HttpClient"/> drops the authorization header on redirects, so the follow-up request would end with 403.
/// </summary>
internal sealed class JenkinsApiClient : IJenkinsApi, IDisposable
{
    private const string StatusQuery = "api/json?tree=offline,temporarilyOffline,idle,offlineCauseReason";

    private readonly HttpClient http;
    private readonly JenkinsAgentOptions settings;

    public JenkinsApiClient(IOptions<WatchdogOptions> options)
        : this(CreateHandler(), options)
    {
    }

    internal JenkinsApiClient(HttpMessageHandler handler, IOptions<WatchdogOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        settings = options.Value.JenkinsAgent;

        var baseUrl = settings.ControllerUrl.EndsWith('/') ? settings.ControllerUrl : settings.ControllerUrl + "/";
        http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = settings.ApiTimeout };
        if (settings.IsApiConfigured)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ApiUser}:{settings.ApiToken}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public bool IsConfigured => settings.IsApiConfigured;

    public async Task<JenkinsNodeStatus> GetNodeStatusAsync(CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(new Uri(NodePath(StatusQuery), UriKind.Relative), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = json.RootElement;
            var reason = root.TryGetProperty("offlineCauseReason", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            return new JenkinsNodeStatus(
                root.GetProperty("offline").GetBoolean(),
                root.GetProperty("temporarilyOffline").GetBoolean(),
                root.GetProperty("idle").GetBoolean(),
                string.IsNullOrEmpty(reason) ? null : reason);
        }
    }

    public async Task ToggleOfflineAsync(string? offlineMessage, CancellationToken cancellationToken)
    {
        var path = NodePath("toggleOffline");
        if (offlineMessage is not null)
            path += "?offlineMessage=" + Uri.EscapeDataString(offlineMessage);

        using var response = await http.PostAsync(new Uri(path, UriKind.Relative), content: null, cancellationToken).ConfigureAwait(false);

        // Jenkins confirms the toggle with a redirect to the node page.
        if (!IsRedirect(response.StatusCode))
            response.EnsureSuccessStatusCode();
    }

    public void Dispose() => http.Dispose();

    internal static SocketsHttpHandler CreateHandler() =>
        new() { PooledConnectionLifetime = TimeSpan.FromMinutes(5), AllowAutoRedirect = false };

    private static bool IsRedirect(System.Net.HttpStatusCode status) => (int)status is >= 300 and < 400;

    private string NodePath(string suffix) => $"computer/{Uri.EscapeDataString(settings.AgentName)}/{suffix}";
}
