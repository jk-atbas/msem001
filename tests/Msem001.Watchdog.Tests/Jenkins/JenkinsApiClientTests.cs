using System.Net;
using System.Text;
using System.Text.Json;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Jenkins;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Jenkins;

public sealed class JenkinsApiClientTests
{
    private const string StatusJson = """{"_class":"hudson.slaves.SlaveComputer","idle":false,"offline":true,"offlineCauseReason":"[MSEM001 Watchdog] Wartung","temporarilyOffline":true}""";

    [Test]
    public async Task GetNodeStatusAsync_ReadsStatusWithBasicAuthentication()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(StatusJson));
        using var client = CreateClient(handler);

        var status = await client.GetNodeStatusAsync(TestData.Ct);

        await Assert.That(status).IsEqualTo(new JenkinsNodeStatus(Offline: true, TemporarilyOffline: true, Idle: false, "[MSEM001 Watchdog] Wartung"));
        var request = handler.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Get);
        await Assert.That(request.RequestUri!.ToString())
            .IsEqualTo("http://jenkins.local:8080/computer/einarbeitungs-laptop/api/json?tree=offline,temporarilyOffline,idle,offlineCauseReason");
        await Assert.That(request.Headers.Authorization!.Scheme).IsEqualTo("Basic");
        await Assert.That(Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!))).IsEqualTo("watchdog:t0ken");
    }

    [Test]
    public async Task GetNodeStatusAsync_EmptyOfflineReason_IsNull()
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json("""{"idle":true,"offline":false,"offlineCauseReason":"","temporarilyOffline":false}"""));
        using var client = CreateClient(handler);

        var status = await client.GetNodeStatusAsync(TestData.Ct);

        await Assert.That(status).IsEqualTo(new JenkinsNodeStatus(false, false, true, null));
    }

    [Test]
    public async Task GetNodeStatusAsync_Unauthorized_Throws()
    {
        using var client = CreateClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)));

        await Assert.That(async () => { await client.GetNodeStatusAsync(TestData.Ct); }).Throws<HttpRequestException>();
    }

    [Test]
    public async Task GetNodeStatusAsync_UnexpectedJson_Throws()
    {
        using var client = CreateClient(new StubHttpHandler(_ => StubHttpHandler.Json("""{"idle":true}""")));

        await Assert.That(async () => { await client.GetNodeStatusAsync(TestData.Ct); }).Throws<KeyNotFoundException>();
    }

    [Test]
    public async Task GetNodeStatusAsync_InvalidJson_Throws()
    {
        using var client = CreateClient(new StubHttpHandler(_ => StubHttpHandler.Json("<html>Login</html>")));

        await Assert.That(async () => { await client.GetNodeStatusAsync(TestData.Ct); }).Throws<JsonException>();
    }

    [Test]
    public async Task ToggleOfflineAsync_WithMessage_PostsEscapedMessage()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(handler);

        await client.ToggleOfflineAsync("[MSEM001 Watchdog] Wartung 'Speicher'", TestData.Ct);

        var request = handler.Requests.Single();
        await Assert.That(request.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(request.RequestUri!.AbsoluteUri)
            .IsEqualTo("http://jenkins.local:8080/computer/einarbeitungs-laptop/toggleOffline?offlineMessage=%5BMSEM001%20Watchdog%5D%20Wartung%20%27Speicher%27");
    }

    [Test]
    public async Task ToggleOfflineAsync_WithoutMessage_PostsPlainToggle()
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var client = CreateClient(handler);

        await client.ToggleOfflineAsync(null, TestData.Ct);

        await Assert.That(handler.Requests.Single().RequestUri!.AbsoluteUri).IsEqualTo("http://jenkins.local:8080/computer/einarbeitungs-laptop/toggleOffline");
    }

    [Test]
    [Arguments(HttpStatusCode.Found)]
    [Arguments(HttpStatusCode.SeeOther)]
    public async Task ToggleOfflineAsync_RedirectFromJenkins_IsSuccess(HttpStatusCode status)
    {
        var handler = new StubHttpHandler(_ => new HttpResponseMessage(status) { Headers = { Location = new Uri("http://jenkins.local:8080/computer/einarbeitungs-laptop/") } });
        using var client = CreateClient(handler);

        await client.ToggleOfflineAsync(null, TestData.Ct);

        await Assert.That(handler.Requests.Count).IsEqualTo(1);
    }

    [Test]
    public async Task CreateHandler_DoesNotFollowRedirects()
    {
        using var handler = JenkinsApiClient.CreateHandler();

        await Assert.That(handler.AllowAutoRedirect).IsFalse();
    }

    [Test]
    public async Task ToggleOfflineAsync_Forbidden_Throws()
    {
        using var client = CreateClient(new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        await Assert.That(async () => { await client.ToggleOfflineAsync(null, TestData.Ct); }).Throws<HttpRequestException>();
    }

    [Test]
    [Arguments("", "")]
    [Arguments("watchdog", "")]
    public async Task WithoutCredentials_IsNotConfiguredAndSendsNoAuthorization(string user, string token)
    {
        var handler = new StubHttpHandler(_ => StubHttpHandler.Json(StatusJson));
        using var client = CreateClient(handler, user, token);

        await client.GetNodeStatusAsync(TestData.Ct);

        await Assert.That(client.IsConfigured).IsFalse();
        await Assert.That(handler.Requests.Single().Headers.Authorization).IsNull();
    }

    [Test]
    public async Task PublicConstructor_CreatesClient()
    {
        using var client = new JenkinsApiClient(TestData.CreateOptions(Configure("watchdog", "t0ken")));

        await Assert.That(client.IsConfigured).IsTrue();
    }

    private static JenkinsApiClient CreateClient(StubHttpHandler handler, string user = "watchdog", string token = "t0ken") =>
        new(handler, TestData.CreateOptions(Configure(user, token)));

    private static Action<WatchdogOptions> Configure(string user, string token) => o =>
    {
        o.JenkinsAgent.ControllerUrl = "http://jenkins.local:8080";
        o.JenkinsAgent.ApiUser = user;
        o.JenkinsAgent.ApiToken = token;
    };
}
