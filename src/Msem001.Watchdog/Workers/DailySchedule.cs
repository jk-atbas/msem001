namespace Msem001.Watchdog.Workers;

/// <summary>
/// Time calculations for daily runs at a local wall clock time.
/// </summary>
internal static class DailySchedule
{
    private static readonly TimeSpan MaxDelaySlice = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Returns the next point in time strictly after <paramref name="now"/> at which the local time is <paramref name="timeOfDay"/>.
    /// A time inside a daylight saving gap is shifted by the gap.
    /// </summary>
    public static DateTimeOffset GetNextOccurrence(DateTimeOffset now, TimeOnly timeOfDay, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        for (var dayOffset = 0; dayOffset <= 2; dayOffset++)
        {
            var local = localToday.AddDays(dayOffset).ToDateTime(timeOfDay, DateTimeKind.Unspecified);
            if (timeZone.IsInvalidTime(local))
                local = local.AddHours(1);

            var candidate = new DateTimeOffset(local, timeZone.GetUtcOffset(local));
            if (candidate > now)
                return candidate;
        }

        throw new InvalidOperationException("Kein nächster Ausführungszeitpunkt gefunden.");
    }

    /// <summary>
    /// Waits until <paramref name="target"/>. Waits in slices so that standby or clock changes do not delay the run.
    /// </summary>
    public static async Task DelayUntilAsync(this TimeProvider timeProvider, DateTimeOffset target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        while (true)
        {
            var remaining = target - timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                return;

            await Task.Delay(remaining < MaxDelaySlice ? remaining : MaxDelaySlice, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
