namespace Msem001.Watchdog.Processes;

/// <summary>
/// Finds processes including their command line (not available via <c>System.Diagnostics.Process</c>).
/// </summary>
internal interface IProcessQuery
{
    /// <summary>Finds processes by image name (e.g. <c>java.exe</c>).</summary>
    /// <returns>The matching processes with their command lines.</returns>
    IReadOnlyList<ProcessSnapshot> FindByImageName(string imageName);
}
