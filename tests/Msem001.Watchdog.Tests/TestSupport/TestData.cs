using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Disk;
using Msem001.Watchdog.Podman;
using Msem001.Watchdog.Processes;
using Testably.Abstractions.Testing;

namespace Msem001.Watchdog.Tests.TestSupport;

/// <summary>
/// Factory methods for test data and system abstraction fakes.
/// </summary>
internal static class TestData
{
    public const string ServiceDirectory = @"C:\Services\Msem001Watchdog";

    public static CancellationToken Ct => TestContext.Current?.Execution.CancellationToken ?? CancellationToken.None;

    /// <summary>Valid options without any waiting times.</summary>
    public static WatchdogOptions CreateOptionsValue(Action<WatchdogOptions>? configure = null)
    {
        var options = new WatchdogOptions();
        options.Podman.ProxySettleTime = TimeSpan.Zero;
        options.JenkinsAgent.StartupGracePeriod = TimeSpan.Zero;
        options.JenkinsAgent.Secret = "s3cr3t";
        options.Mail.Recipients = ["devops"];
        options.Mail.BatchDelay = TimeSpan.Zero;
        options.Mail.RetryDelay = TimeSpan.Zero;
        configure?.Invoke(options);
        return options;
    }

    public static IOptions<WatchdogOptions> CreateOptions(Action<WatchdogOptions>? configure = null) =>
        Options.Create(CreateOptionsValue(configure));

    /// <summary>In-memory Windows file system (Testably).</summary>
    public static MockFileSystem CreateFileSystem() => new(o => o.SimulatingOperatingSystem(SimulationMode.Windows));

    public static HostingEnvironment CreateHostEnvironment(string contentRootPath = ServiceDirectory) => new()
    {
        ApplicationName = "Msem001.Watchdog",
        EnvironmentName = "Test",
        ContentRootPath = contentRootPath,
        ContentRootFileProvider = new NullFileProvider(),
    };

    /// <summary>Environment of the target machine (EnvironmentAbstractions).</summary>
    public static MockEnvironmentProvider CreateEnvironment() => new(useExistingEnvironmentValues: false, addExistingEnvironmentVariables: false)
    {
        MachineName = "MSEM001",
        UserDomainName = "MSEM001",
        UserName = "atbas",
    };

    public static DiskSpaceInfo Disk(double freeGb, double totalGb = 1000) =>
        new(@"C:\", (long)(totalGb * DiskSpaceInfo.BytesPerGigabyte), (long)(freeGb * DiskSpaceInfo.BytesPerGigabyte));

    public static ProcessRunResult Success(params string[] standardOutput) =>
        new(0, false, standardOutput, [], TimeSpan.FromSeconds(1));

    public static ProcessRunResult Failure(int exitCode, params string[] standardError) =>
        new(exitCode, false, [], standardError, TimeSpan.FromSeconds(1));

    public static PodmanHealth Healthy() => new(PodmanMachineState.Running, true, 1, "OK");

    public static PodmanHealth ProxyBroken() => new(PodmanMachineState.Running, false, 0, "pipe not found");

    public static PodmanHealth MachineStopped() => new(PodmanMachineState.Stopped, false, 0, "stopped");
}
