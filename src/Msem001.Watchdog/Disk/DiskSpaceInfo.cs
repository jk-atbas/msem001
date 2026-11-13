using System.Globalization;

namespace Msem001.Watchdog.Disk;

/// <summary>
/// Snapshot of the space of a drive. Gigabytes are binary (GiB), as displayed by Windows Explorer.
/// </summary>
internal sealed record DiskSpaceInfo(string Drive, long TotalBytes, long FreeBytes)
{
    public const double BytesPerGigabyte = 1024d * 1024d * 1024d;

    public double FreeGigabytes => FreeBytes / BytesPerGigabyte;

    public double TotalGigabytes => TotalBytes / BytesPerGigabyte;

    public double UsedPercent => TotalBytes <= 0 ? 0 : (TotalBytes - FreeBytes) * 100d / TotalBytes;

    public override string ToString() => ToDisplayString();

    public string ToDisplayString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Drive} {FreeGigabytes:F1} GB frei von {TotalGigabytes:F1} GB ({UsedPercent:F1} % belegt)");
}
