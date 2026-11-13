using Msem001.Watchdog.Configuration;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Configuration;

public sealed class WatchdogOptionsValidatorTests
{
    private readonly WatchdogOptionsValidator validator = new();

    public static IEnumerable<Func<(string ExpectedKey, Action<WatchdogOptions> Configure)>> InvalidOptions()
    {
        yield return () => ("DriveName", o => o.Disk.DriveName = " ");
        yield return () => ("CleanupThresholdGb", o => o.Disk.CleanupThresholdGb = 0);
        yield return () => ("PodmanResetThresholdGb", o => o.Disk.PodmanResetThresholdGb = 10);
        yield return () => ("ErrorUsagePercent", o => o.Disk.ErrorUsagePercent = 120);
        yield return () => ("CriticalUsagePercent", o => o.Disk.CriticalUsagePercent = 80);
        yield return () => ("Disk:CheckInterval", o => o.Disk.CheckInterval = TimeSpan.Zero);
        yield return () => ("MaintenanceCooldown", o => o.Disk.MaintenanceCooldown = TimeSpan.FromSeconds(-1));
        yield return () => ("ImmediateMaintenanceThresholdGb", o => o.Disk.ImmediateMaintenanceThresholdGb = -1);
        yield return () => ("ImmediateMaintenanceThresholdGb", o => o.Disk.ImmediateMaintenanceThresholdGb = 60);
        yield return () => ("ApiUser und ApiToken", o => o.JenkinsAgent.ApiUser = "watchdog");
        yield return () => ("ApiUser und ApiToken", o => o.JenkinsAgent.ApiToken = "t0ken");
        yield return () => ("ApiTimeout", o => o.JenkinsAgent.ApiTimeout = TimeSpan.Zero);
        yield return () => ("DrainTimeout", o => o.JenkinsAgent.DrainTimeout = TimeSpan.FromSeconds(-1));
        yield return () => ("DrainPollInterval", o => o.JenkinsAgent.DrainPollInterval = TimeSpan.Zero);
        yield return () => ("Memory:ErrorUsagePercent", o => o.Memory.ErrorUsagePercent = 0);
        yield return () => ("Memory:CriticalUsagePercent", o => o.Memory.CriticalUsagePercent = 85);
        yield return () => ("Memory:CheckInterval", o => o.Memory.CheckInterval = TimeSpan.Zero);
        yield return () => ("Memory:SustainedDuration", o => o.Memory.SustainedDuration = TimeSpan.FromSeconds(-1));
        yield return () => ("MachineName", o => o.Podman.MachineName = string.Empty);
        yield return () => ("FailureThreshold", o => o.Podman.FailureThreshold = 0);
        yield return () => ("ProbeTimeout", o => o.Podman.ProbeTimeout = TimeSpan.Zero);
        yield return () => ("PowerShellExecutable", o => o.Scripts.PowerShellExecutable = string.Empty);
        yield return () => ("PodmanResetTimeout", o => o.Scripts.PodmanResetTimeout = TimeSpan.Zero);
        yield return () => ("ControllerUrl", o => o.JenkinsAgent.ControllerUrl = "not a url");
        yield return () => ("AgentName", o => o.JenkinsAgent.AgentName = string.Empty);
        yield return () => ("Port", o => o.Mail.Port = 0);
        yield return () => ("Host", o => o.Mail.Host = string.Empty);
        yield return () => ("Recipients", o => o.Mail.Recipients = [" "]);
        yield return () => ("SendAttempts", o => o.Mail.SendAttempts = 0);
        yield return () => ("MaxEntriesPerMail", o => o.Mail.MaxEntriesPerMail = 0);
    }

    [Test]
    public async Task Validate_ValidOptions_Succeeds()
    {
        var result = validator.Validate(null, TestData.CreateOptionsValue());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_ApiCredentialsTogether_Succeeds()
    {
        var result = validator.Validate(null, TestData.CreateOptionsValue(o =>
        {
            o.JenkinsAgent.ApiUser = "watchdog";
            o.JenkinsAgent.ApiToken = "t0ken";
        }));

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_DefaultOptions_RequireSecretAndRecipients()
    {
        var result = validator.Validate(null, new WatchdogOptions());

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains("Secret").And.Contains("Recipients");
    }

    [Test]
    public async Task Validate_DisabledAgentAndMail_DoNotRequireSecretAndRecipients()
    {
        var options = new WatchdogOptions();
        options.JenkinsAgent.Enabled = false;
        options.Mail.Enabled = false;
        options.Memory.Enabled = false;
        options.Memory.CheckInterval = TimeSpan.Zero;

        await Assert.That(validator.Validate(null, options).Succeeded).IsTrue();
    }

    [Test]
    [MethodDataSource(nameof(InvalidOptions))]
    public async Task Validate_InvalidValue_FailsWithKey(string expectedKey, Action<WatchdogOptions> configure)
    {
        var result = validator.Validate(null, TestData.CreateOptionsValue(configure));

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains(expectedKey);
    }
}
