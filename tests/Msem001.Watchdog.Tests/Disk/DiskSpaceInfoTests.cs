using Msem001.Watchdog.Disk;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Disk;

public sealed class DiskSpaceInfoTests
{
    [Test]
    public async Task Properties_AreCalculatedInBinaryGigabytes()
    {
        var disk = TestData.Disk(freeGb: 45.5, totalGb: 1000);

        await Assert.That(disk.FreeGigabytes).IsEqualTo(45.5).Within(0.001);
        await Assert.That(disk.TotalGigabytes).IsEqualTo(1000).Within(0.001);
        await Assert.That(disk.UsedPercent).IsEqualTo(95.45).Within(0.001);
    }

    [Test]
    public async Task UsedPercent_EmptyDrive_IsZero()
    {
        await Assert.That(new DiskSpaceInfo(@"C:\", 0, 0).UsedPercent).IsEqualTo(0);
    }

    [Test]
    public async Task ToDisplayString_ContainsFreeSpaceAndUsage()
    {
        var disk = TestData.Disk(freeGb: 45.5, totalGb: 1000);

        await Assert.That(disk.ToDisplayString()).IsEqualTo(@"C:\ 45.5 GB frei von 1000.0 GB (95.5 % belegt)");
        await Assert.That(disk.ToString()).IsEqualTo(disk.ToDisplayString());
    }
}
