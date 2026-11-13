using System.IO.Abstractions;
using Microsoft.Extensions.Options;
using Msem001.Watchdog.Configuration;

namespace Msem001.Watchdog.Disk;

/// <summary>
/// <see cref="IDiskSpaceProbe"/> based on <see cref="IDriveInfo"/> (System.IO.Abstractions).
/// </summary>
internal sealed class DriveInfoDiskSpaceProbe(IFileSystem fileSystem, IOptions<WatchdogOptions> options) : IDiskSpaceProbe
{
    public DiskSpaceInfo GetDiskSpace()
    {
        var drive = fileSystem.DriveInfo.New(options.Value.Disk.DriveName);
        return new DiskSpaceInfo(drive.Name, drive.TotalSize, drive.AvailableFreeSpace);
    }
}
