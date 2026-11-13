using System.IO.Pipes;
using System.Text;
using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Podman;

/// <summary>
/// Integration tests against a real named pipe that plays the role of win-sshproxy.
/// </summary>
[Category("Integration")]
public sealed class NamedPipeDockerApiProbeTests
{
    private readonly string pipeName = "msem001-test-" + Guid.NewGuid().ToString("N");

    [Test]
    public async Task ProbeAsync_PingAnswersOk_IsReachable()
    {
        var server = ServeOnceAsync("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");

        var result = await CreateProbe().ProbeAsync(TestData.Ct);
        var request = await server;

        await Assert.That(result.IsReachable).IsTrue();
        await Assert.That(request).StartsWith("GET /_ping HTTP/1.1");
    }

    [Test]
    public async Task ProbeAsync_ServerError_IsNotReachable()
    {
        var server = ServeOnceAsync("HTTP/1.1 500 Internal Server Error\r\nContent-Length: 4\r\nConnection: close\r\n\r\nfail");

        var result = await CreateProbe().ProbeAsync(TestData.Ct);
        await server;

        await Assert.That(result.IsReachable).IsFalse();
        await Assert.That(result.Detail).Contains("HTTP 500").And.Contains("fail");
    }

    [Test]
    public async Task ProbeAsync_PipeMissing_IsNotReachableWithinTimeout()
    {
        var result = await CreateProbe().ProbeAsync(TestData.Ct);

        await Assert.That(result.IsReachable).IsFalse();
        await Assert.That(result.Detail).Contains(pipeName);
    }

    private NamedPipeDockerApiProbe CreateProbe() => new(TestData.CreateOptions(o =>
    {
        o.Podman.DockerApiPipeName = pipeName;
        o.Podman.ProbeTimeout = TimeSpan.FromSeconds(2);
    }));

    private async Task<string> ServeOnceAsync(string response)
    {
        var token = TestData.Ct;
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await server.WaitForConnectionAsync(token);

        var buffer = new byte[4096];
        var request = new StringBuilder();
        while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await server.ReadAsync(buffer, token);
            if (read == 0)
                break;

            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        await server.WriteAsync(Encoding.ASCII.GetBytes(response), token);
        await server.FlushAsync(token);
        server.WaitForPipeDrain();
        return request.ToString();
    }
}
