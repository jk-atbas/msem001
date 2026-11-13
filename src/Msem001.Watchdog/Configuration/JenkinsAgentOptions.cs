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
}
