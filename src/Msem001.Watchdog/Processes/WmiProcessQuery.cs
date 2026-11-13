using System.Globalization;
using System.Management;

namespace Msem001.Watchdog.Processes;

/// <summary>
/// <see cref="IProcessQuery"/> via WMI (<c>Win32_Process</c>).
/// </summary>
internal sealed class WmiProcessQuery : IProcessQuery
{
    public IReadOnlyList<ProcessSnapshot> FindByImageName(string imageName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageName);

        var query = $"SELECT ProcessId, Name, CommandLine FROM Win32_Process WHERE Name = '{EscapeWql(imageName)}'";
        using var searcher = new ManagementObjectSearcher(query);
        using var results = searcher.Get();

        var processes = new List<ProcessSnapshot>();
        foreach (var item in results)
        {
            using (item)
            {
                processes.Add(new ProcessSnapshot(
                    Convert.ToInt32(item["ProcessId"], CultureInfo.InvariantCulture),
                    item["Name"] as string ?? imageName,
                    item["CommandLine"] as string));
            }
        }

        return processes;
    }

    internal static string EscapeWql(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
}
