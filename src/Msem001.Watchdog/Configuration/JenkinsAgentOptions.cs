namespace Msem001.Watchdog.Configuration;

/// <summary>
/// Jenkins inbound agent process (<c>java -jar agent.jar ...</c>).
/// </summary>
public sealed class JenkinsAgentOptions
{
    /// <summary>Whether the service manages the agent at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Java executable.</summary>
    public string JavaExecutable { get; set; } = "java";

    /// <summary>Path of <c>agent.jar</c>. Its directory is used as working directory.</summary>
    public string AgentJarPath { get; set; } = @"C:\jenkins\agent.jar";

    /// <summary>URL of the Jenkins controller.</summary>
    public string ControllerUrl { get; set; } = "http://10.24.9.62:8080/";

    /// <summary>Agent secret. Must not be committed, set it in <c>appsettings.Local.json</c>.</summary>
    public string Secret { get; set; } = string.Empty;

    /// <summary>Name of the agent node in Jenkins.</summary>
    public string AgentName { get; set; } = "einarbeitungs-laptop";

    /// <summary>Agent work directory (<c>-workDir</c>).</summary>
    public string WorkDir { get; set; } = @"C:\jenkins";

    /// <summary>Use WebSocket transport (<c>-webSocket</c>).</summary>
    public bool UseWebSocket { get; set; } = true;

    /// <summary>Timeout for terminating the agent process tree.</summary>
    public TimeSpan StopTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Time after the start in which an immediate exit of the agent is detected.</summary>
    public TimeSpan StartupGracePeriod { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Jenkins user for the REST API (needs Agent/Disconnect). Together with <see cref="ApiToken"/> it enables the graceful
    /// maintenance: the node is marked temporarily offline and running builds may finish. Empty = agent is stopped immediately.
    /// </summary>
    public string ApiUser { get; set; } = string.Empty;

    /// <summary>API token of <see cref="ApiUser"/>. Must not be committed, set it in <c>appsettings.Local.json</c>.</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>Timeout of a single REST API request.</summary>
    public TimeSpan ApiTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Maximum time a maintenance waits for running builds before the agent is stopped anyway. 0 = do not wait.</summary>
    public TimeSpan DrainTimeout { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Interval for polling whether the node is idle while waiting.</summary>
    public TimeSpan DrainPollInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets a value indicating whether the REST API credentials are configured.</summary>
    public bool IsApiConfigured => !string.IsNullOrWhiteSpace(ApiUser) && !string.IsNullOrWhiteSpace(ApiToken);
}
