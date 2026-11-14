using Microsoft.Extensions.Options;

namespace Msem001.Watchdog.Configuration;

/// <summary>
/// Validates <see cref="WatchdogOptions"/> on service start.
/// </summary>
internal sealed class WatchdogOptionsValidator : IValidateOptions<WatchdogOptions>
{
    public ValidateOptionsResult Validate(string? name, WatchdogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        ValidateDisk(options.Disk, errors);
        ValidateMemory(options.Memory, errors);
        ValidatePodman(options.Podman, errors);
        ValidateContainers(options.Containers, errors);
        ValidateScripts(options.Scripts, errors);
        ValidateJenkinsAgent(options.JenkinsAgent, errors);
        ValidateMail(options.Mail, errors);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private static void ValidateDisk(DiskOptions disk, List<string> errors)
    {
        RequireText(disk.DriveName, "Watchdog:Disk:DriveName", errors);

        if (disk.CleanupThresholdGb <= 0)
            errors.Add("Watchdog:Disk:CleanupThresholdGb muss größer 0 sein.");

        if (disk.PodmanResetThresholdGb < disk.CleanupThresholdGb)
            errors.Add("Watchdog:Disk:PodmanResetThresholdGb muss größer oder gleich CleanupThresholdGb sein.");

        if (disk.ErrorUsagePercent is <= 0 or > 100 || disk.CriticalUsagePercent is <= 0 or > 100)
            errors.Add("Watchdog:Disk:ErrorUsagePercent und CriticalUsagePercent müssen zwischen 0 und 100 liegen.");

        if (disk.CriticalUsagePercent < disk.ErrorUsagePercent)
            errors.Add("Watchdog:Disk:CriticalUsagePercent muss größer oder gleich ErrorUsagePercent sein.");

        RequirePositive(disk.CheckInterval, "Watchdog:Disk:CheckInterval", errors);
        RequireNotNegative(disk.MaintenanceCooldown, "Watchdog:Disk:MaintenanceCooldown", errors);

        if (disk.ImmediateMaintenanceThresholdGb < 0 || disk.ImmediateMaintenanceThresholdGb > disk.CleanupThresholdGb)
            errors.Add("Watchdog:Disk:ImmediateMaintenanceThresholdGb muss zwischen 0 und CleanupThresholdGb liegen.");
        RequirePositive(disk.UsageAlertRepeatInterval, "Watchdog:Disk:UsageAlertRepeatInterval", errors);
    }

    private static void ValidateMemory(MemoryOptions memory, List<string> errors)
    {
        if (!memory.Enabled)
            return;

        if (memory.ErrorUsagePercent is <= 0 or > 100 || memory.CriticalUsagePercent is <= 0 or > 100)
            errors.Add("Watchdog:Memory:ErrorUsagePercent und CriticalUsagePercent müssen zwischen 0 und 100 liegen.");

        if (memory.CriticalUsagePercent < memory.ErrorUsagePercent)
            errors.Add("Watchdog:Memory:CriticalUsagePercent muss größer oder gleich ErrorUsagePercent sein.");

        RequirePositive(memory.CheckInterval, "Watchdog:Memory:CheckInterval", errors);
        RequireNotNegative(memory.SustainedDuration, "Watchdog:Memory:SustainedDuration", errors);
        RequirePositive(memory.UsageAlertRepeatInterval, "Watchdog:Memory:UsageAlertRepeatInterval", errors);
    }

    private static void ValidatePodman(PodmanOptions podman, List<string> errors)
    {
        RequireText(podman.Executable, "Watchdog:Podman:Executable", errors);
        RequireText(podman.MachineName, "Watchdog:Podman:MachineName", errors);
        RequireText(podman.DockerApiPipeName, "Watchdog:Podman:DockerApiPipeName", errors);

        if (podman.FailureThreshold < 1)
            errors.Add("Watchdog:Podman:FailureThreshold muss mindestens 1 sein.");

        RequireNotNegative(podman.StartupDelay, "Watchdog:Podman:StartupDelay", errors);
        RequirePositive(podman.CheckInterval, "Watchdog:Podman:CheckInterval", errors);
        RequireNotNegative(podman.RepairRetryInterval, "Watchdog:Podman:RepairRetryInterval", errors);
        RequireNotNegative(podman.RepairDrainTimeout, "Watchdog:Podman:RepairDrainTimeout", errors);
        RequirePositive(podman.ProbeTimeout, "Watchdog:Podman:ProbeTimeout", errors);
        RequirePositive(podman.CommandTimeout, "Watchdog:Podman:CommandTimeout", errors);
        RequirePositive(podman.StartTimeout, "Watchdog:Podman:StartTimeout", errors);
        RequireNotNegative(podman.ProxySettleTime, "Watchdog:Podman:ProxySettleTime", errors);
    }

    private static void ValidateContainers(ContainerCleanupOptions containers, List<string> errors)
    {
        if (!containers.Enabled)
            return;

        RequirePositive(containers.CheckInterval, "Watchdog:Containers:CheckInterval", errors);
        RequirePositive(containers.MaxRunTime, "Watchdog:Containers:MaxRunTime", errors);
        if (containers.RemoveStopped)
            RequirePositive(containers.MaxStoppedTime, "Watchdog:Containers:MaxStoppedTime", errors);
        RequireNotNegative(containers.StopTimeout, "Watchdog:Containers:StopTimeout", errors);
    }

    private static void ValidateScripts(ScriptOptions scripts, List<string> errors)
    {
        RequireText(scripts.PowerShellExecutable, "Watchdog:Scripts:PowerShellExecutable", errors);
        RequireText(scripts.CleanupScript, "Watchdog:Scripts:CleanupScript", errors);
        RequireText(scripts.PodmanResetScript, "Watchdog:Scripts:PodmanResetScript", errors);
        RequirePositive(scripts.CleanupTimeout, "Watchdog:Scripts:CleanupTimeout", errors);
        RequirePositive(scripts.PodmanResetTimeout, "Watchdog:Scripts:PodmanResetTimeout", errors);
    }

    private static void ValidateJenkinsAgent(JenkinsAgentOptions agent, List<string> errors)
    {
        if (!agent.Enabled)
            return;

        RequireText(agent.JavaExecutable, "Watchdog:JenkinsAgent:JavaExecutable", errors);
        RequireText(agent.AgentJarPath, "Watchdog:JenkinsAgent:AgentJarPath", errors);
        RequireText(agent.AgentName, "Watchdog:JenkinsAgent:AgentName", errors);
        RequireText(agent.WorkDir, "Watchdog:JenkinsAgent:WorkDir", errors);
        RequireText(agent.Secret, "Watchdog:JenkinsAgent:Secret (in appsettings.Local.json hinterlegen)", errors);

        if (!Uri.TryCreate(agent.ControllerUrl, UriKind.Absolute, out _))
            errors.Add("Watchdog:JenkinsAgent:ControllerUrl muss eine absolute URL sein.");

        RequirePositive(agent.StopTimeout, "Watchdog:JenkinsAgent:StopTimeout", errors);
        RequireNotNegative(agent.StartupGracePeriod, "Watchdog:JenkinsAgent:StartupGracePeriod", errors);

        if (string.IsNullOrWhiteSpace(agent.ApiUser) != string.IsNullOrWhiteSpace(agent.ApiToken))
            errors.Add("Watchdog:JenkinsAgent:ApiUser und ApiToken müssen gemeinsam gesetzt sein (ApiToken in appsettings.Local.json hinterlegen).");

        RequirePositive(agent.ApiTimeout, "Watchdog:JenkinsAgent:ApiTimeout", errors);
        RequireNotNegative(agent.DrainTimeout, "Watchdog:JenkinsAgent:DrainTimeout", errors);
        RequirePositive(agent.DrainPollInterval, "Watchdog:JenkinsAgent:DrainPollInterval", errors);
    }

    private static void ValidateMail(MailOptions mail, List<string> errors)
    {
        if (!mail.Enabled)
            return;

        RequireText(mail.Host, "Watchdog:Mail:Host", errors);
        RequireText(mail.From, "Watchdog:Mail:From", errors);

        if (mail.Port is <= 0 or > 65535)
            errors.Add("Watchdog:Mail:Port ist ungültig.");

        if (mail.Recipients.Count == 0 || mail.Recipients.Any(string.IsNullOrWhiteSpace))
            errors.Add("Watchdog:Mail:Recipients muss mindestens einen gültigen Empfänger enthalten.");

        if (mail.MaxEntriesPerMail < 1)
            errors.Add("Watchdog:Mail:MaxEntriesPerMail muss mindestens 1 sein.");

        if (mail.SendAttempts < 1)
            errors.Add("Watchdog:Mail:SendAttempts muss mindestens 1 sein.");

        RequireNotNegative(mail.BatchDelay, "Watchdog:Mail:BatchDelay", errors);
        RequireNotNegative(mail.RetryDelay, "Watchdog:Mail:RetryDelay", errors);
        RequirePositive(mail.Timeout, "Watchdog:Mail:Timeout", errors);
    }

    private static void RequireText(string? value, string key, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{key} darf nicht leer sein.");
    }

    private static void RequirePositive(TimeSpan value, string key, List<string> errors)
    {
        if (value <= TimeSpan.Zero)
            errors.Add($"{key} muss größer 0 sein.");
    }

    private static void RequireNotNegative(TimeSpan value, string key, List<string> errors)
    {
        if (value < TimeSpan.Zero)
            errors.Add($"{key} darf nicht negativ sein.");
    }
}
