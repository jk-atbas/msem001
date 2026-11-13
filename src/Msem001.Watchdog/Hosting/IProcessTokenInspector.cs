namespace Msem001.Watchdog.Hosting;

/// <summary>
/// Reads the token facts of the current process.
/// </summary>
internal interface IProcessTokenInspector
{
    /// <summary>
    /// Inspects the token of the current process.
    /// </summary>
    /// <returns>The token facts.</returns>
    ProcessTokenInfo Inspect();
}
