namespace Msem001.Watchdog.Jenkins;

/// <summary>
/// State of the agent node as seen by the Jenkins controller (<c>/computer/&lt;name&gt;/api/json</c>).
/// </summary>
/// <param name="Offline">The node is offline (disconnected or marked temporarily offline).</param>
/// <param name="TemporarilyOffline">The node was marked offline by a user; it accepts no new builds.</param>
/// <param name="Idle">No executor of the node runs a build.</param>
/// <param name="OfflineCauseReason">Message given when the node was marked offline, if any.</param>
internal sealed record JenkinsNodeStatus(bool Offline, bool TemporarilyOffline, bool Idle, string? OfflineCauseReason);
