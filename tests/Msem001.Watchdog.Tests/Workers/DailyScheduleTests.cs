using Microsoft.Extensions.Time.Testing;
using Msem001.Watchdog.Tests.TestSupport;
using Msem001.Watchdog.Workers;

namespace Msem001.Watchdog.Tests.Workers;

public sealed class DailyScheduleTests
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
    private static readonly TimeOnly PreCheck = new(20, 45);

    [Test]
    public async Task GetNextOccurrence_BeforeTime_IsToday()
    {
        var now = new DateTimeOffset(2026, 11, 13, 10, 0, 0, TimeSpan.FromHours(1));

        await Assert.That(DailySchedule.GetNextOccurrence(now, PreCheck, Berlin))
            .IsEqualTo(new DateTimeOffset(2026, 11, 13, 20, 45, 0, TimeSpan.FromHours(1)));
    }

    [Test]
    public async Task GetNextOccurrence_AtOrAfterTime_IsTomorrow()
    {
        var at = new DateTimeOffset(2026, 11, 13, 20, 45, 0, TimeSpan.FromHours(1));

        await Assert.That(DailySchedule.GetNextOccurrence(at, PreCheck, Berlin)).IsEqualTo(at.AddDays(1));
        await Assert.That(DailySchedule.GetNextOccurrence(at.AddHours(2), PreCheck, Berlin)).IsEqualTo(at.AddDays(1));
    }

    [Test]
    public async Task GetNextOccurrence_SummerTime_UsesLocalWallClock()
    {
        var now = new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);

        await Assert.That(DailySchedule.GetNextOccurrence(now, PreCheck, Berlin))
            .IsEqualTo(new DateTimeOffset(2026, 7, 1, 20, 45, 0, TimeSpan.FromHours(2)));
    }

    [Test]
    public async Task GetNextOccurrence_TimeInDaylightSavingGap_IsShifted()
    {
        var now = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.FromHours(1));

        await Assert.That(DailySchedule.GetNextOccurrence(now, new TimeOnly(2, 30), Berlin))
            .IsEqualTo(new DateTimeOffset(2026, 3, 29, 3, 30, 0, TimeSpan.FromHours(2)));
    }

    [Test]
    public async Task DelayUntilAsync_CompletesOnlyWhenTargetIsReached()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 20, 0, 0, TimeSpan.Zero));
        var target = time.GetUtcNow().AddMinutes(5);

        var delay = time.DelayUntilAsync(target, TestData.Ct);
        await Wait.UntilAsync(() => time.GetUtcNow() >= target.AddMinutes(-1), time, TimeSpan.FromMinutes(1));
        await Assert.That(delay.IsCompleted).IsFalse();

        await Wait.UntilAsync(() => delay.IsCompleted, time, TimeSpan.FromMinutes(1));
        await Assert.That(time.GetUtcNow()).IsGreaterThanOrEqualTo(target);
    }

    [Test]
    public async Task DelayUntilAsync_TargetInPast_CompletesImmediately()
    {
        var time = new FakeTimeProvider();

        await Assert.That(time.DelayUntilAsync(time.GetUtcNow().AddMinutes(-1), TestData.Ct).IsCompleted).IsTrue();
    }
}
