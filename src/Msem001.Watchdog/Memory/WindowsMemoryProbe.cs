using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Msem001.Watchdog.Memory;

/// <summary>
/// <see cref="IMemoryProbe"/> via the Win32 API <c>GlobalMemoryStatusEx</c> (same values as the Task Manager).
/// </summary>
/// <remarks>
/// .NET itself only exposes the memory load observed by the last garbage collection, which is stale for a mostly idle service.
/// </remarks>
internal sealed partial class WindowsMemoryProbe : IMemoryProbe
{
    public MemoryInfo GetMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        return new MemoryInfo((long)status.TotalPhys, (long)status.AvailPhys);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

#pragma warning disable SA1401 // Interop struct: layout must match MEMORYSTATUSEX, public fields are required.
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
#pragma warning restore SA1401
}
