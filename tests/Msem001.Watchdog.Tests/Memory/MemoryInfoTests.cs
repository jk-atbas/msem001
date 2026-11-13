using Msem001.Watchdog.Memory;
using Msem001.Watchdog.Tests.TestSupport;

namespace Msem001.Watchdog.Tests.Memory;

public sealed class MemoryInfoTests
{
    [Test]
    public async Task Properties_AreCalculatedInBinaryGigabytes()
    {
        var memory = TestData.Memory(usedPercent: 92.5, totalGb: 32);

        await Assert.That(memory.TotalGigabytes).IsEqualTo(32).Within(0.001);
        await Assert.That(memory.AvailableGigabytes).IsEqualTo(2.4).Within(0.001);
        await Assert.That(memory.UsedPercent).IsEqualTo(92.5).Within(0.001);
    }

    [Test]
    public async Task UsedPercent_NoMemoryReported_IsZero()
    {
        await Assert.That(new MemoryInfo(0, 0).UsedPercent).IsEqualTo(0);
    }

    [Test]
    public async Task ToDisplayString_ContainsUsageAndFreeMemory()
    {
        var memory = TestData.Memory(usedPercent: 62.5, totalGb: 32);

        await Assert.That(memory.ToDisplayString()).IsEqualTo("62.5 % belegt (12.0 GB frei von 32.0 GB)");
        await Assert.That(memory.ToString()).IsEqualTo(memory.ToDisplayString());
    }

    [Test]
    [Category("Integration")]
    public async Task WindowsMemoryProbe_ReturnsPlausibleValuesOfThisMachine()
    {
        var memory = new WindowsMemoryProbe().GetMemory();

        await Assert.That(memory.TotalBytes).IsGreaterThan(0L);
        await Assert.That(memory.AvailableBytes).IsBetween(0L, memory.TotalBytes);
        await Assert.That(memory.UsedPercent).IsBetween(0d, 100d);
    }
}
