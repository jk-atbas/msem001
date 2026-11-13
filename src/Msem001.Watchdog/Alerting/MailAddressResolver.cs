namespace Msem001.Watchdog.Alerting;

/// <summary>
/// Completes short addresses (<c>j.doe</c>) with the configured domain suffix (<c>@atbas.de</c>).
/// </summary>
internal static class MailAddressResolver
{
    public static string Resolve(string address, string suffix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        var trimmed = address.Trim();
        if (trimmed.Contains('@', StringComparison.Ordinal) || string.IsNullOrWhiteSpace(suffix))
            return trimmed;

        var normalizedSuffix = suffix.Trim();
        return normalizedSuffix.StartsWith('@') ? trimmed + normalizedSuffix : $"{trimmed}@{normalizedSuffix}";
    }

    public static IReadOnlyList<string> ResolveAll(IEnumerable<string> addresses, string suffix) =>
        [.. addresses.Where(a => !string.IsNullOrWhiteSpace(a)).Select(a => Resolve(a, suffix)).Distinct(StringComparer.OrdinalIgnoreCase)];
}
