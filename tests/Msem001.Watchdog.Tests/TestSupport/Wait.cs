using Microsoft.Extensions.Time.Testing;

namespace Msem001.Watchdog.Tests.TestSupport;

/// <summary>
/// Polling helper for background services.
/// </summary>
internal static class Wait
{
    /// <summary>Polls the condition and advances the fake clock by <paramref name="advanceStep"/> between polls.</summary>
    /// <returns>A task that completes when the condition is met.</returns>
    public static async Task UntilAsync(Func<bool> condition, FakeTimeProvider? time = null, TimeSpan advanceStep = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestData.Ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!condition())
        {
            if (timeout.IsCancellationRequested)
                throw new TimeoutException("Bedingung wurde nicht innerhalb von 10 s erfüllt.");

            if (time is not null && advanceStep > TimeSpan.Zero)
                time.Advance(advanceStep);

            await Task.Delay(20, CancellationToken.None);
        }
    }
}
