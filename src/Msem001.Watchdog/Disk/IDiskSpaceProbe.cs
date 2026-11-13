namespace Msem001.Watchdog.Disk;

/// <summary>
/// Reads the current space of the monitored drive.
/// </summary>
internal interface IDiskSpaceProbe
{
    DiskSpaceInfo GetDiskSpace();
}
