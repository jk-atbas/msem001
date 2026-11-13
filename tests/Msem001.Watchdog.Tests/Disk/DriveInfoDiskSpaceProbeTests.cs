using Msem001.Watchdog.Disk;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Disk;

public sealed class DriveInfoDiskSpaceProbeTests
{
    [Test]
    public async Task GetDiskSpace_ReadsTotalAndAvailableSpaceOfConfiguredDrive()
    {
        var fileSystem = TestData.CreateFileSystem();
        fileSystem.WithDrive(@"C:\", d => d.SetTotalSize(10_000));
        fileSystem.Directory.CreateDirectory(@"C:\jenkins\workspace");
        fileSystem.File.WriteAllBytes(@"C:\jenkins\workspace\build.bin", new byte[2_500]);
        var probe = new DriveInfoDiskSpaceProbe(fileSystem, TestData.CreateOptions());

        var disk = probe.GetDiskSpace();

        await Assert.That(disk.Drive).IsEqualTo(@"C:\");
        await Assert.That(disk.TotalBytes).IsEqualTo(10_000L);
        await Assert.That(disk.FreeBytes).IsEqualTo(7_500L);
        await Assert.That(disk.UsedPercent).IsEqualTo(25).Within(0.001);
    }

    [Test]
    public async Task GetDiskSpace_OtherDrive_IsUsedWhenConfigured()
    {
        var fileSystem = TestData.CreateFileSystem();
        fileSystem.WithDrive(@"D:\", d => d.SetTotalSize(4_000));
        var probe = new DriveInfoDiskSpaceProbe(fileSystem, TestData.CreateOptions(o => o.Disk.DriveName = @"D:\"));

        var disk = probe.GetDiskSpace();

        await Assert.That(disk.Drive).IsEqualTo(@"D:\");
        await Assert.That(disk.FreeBytes).IsEqualTo(4_000L);
    }
}
