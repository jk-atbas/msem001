using System.Globalization;
using Msem001.Watchdog.Disk;

namespace Msem001.Watchdog.Memory;

/// <summary>
/// Snapshot of the physical memory (RAM) of the machine.
/// </summary>
internal sealed record MemoryInfo(long TotalBytes, long AvailableBytes)
{
    public double TotalGigabytes => TotalBytes / DiskSpaceInfo.BytesPerGigabyte;

    public double AvailableGigabytes => AvailableBytes / DiskSpaceInfo.BytesPerGigabyte;

    public double UsedPercent => TotalBytes <= 0 ? 0 : (TotalBytes - AvailableBytes) * 100d / TotalBytes;

    public override string ToString() => ToDisplayString();

    public string ToDisplayString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{UsedPercent:F1} % belegt ({AvailableGigabytes:F1} GB frei von {TotalGigabytes:F1} GB)");
}
