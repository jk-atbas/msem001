namespace Msem001.Watchdog.Memory;

/// <summary>
/// Reads the current usage of the physical memory.
/// </summary>
internal interface IMemoryProbe
{
    /// <summary>Reads total and available physical memory.</summary>
    /// <returns>The current memory snapshot.</returns>
    /// <exception cref="System.ComponentModel.Win32Exception">The operating system call failed.</exception>
    MemoryInfo GetMemory();
}
