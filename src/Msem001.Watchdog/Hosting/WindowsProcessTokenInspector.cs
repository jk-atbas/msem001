using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.Win32;

namespace Msem001.Watchdog.Hosting;

/// <summary>
/// <see cref="IProcessTokenInspector"/> via <see cref="WindowsIdentity"/>, <c>GetTokenInformation</c> and the UAC policy in the registry.
/// </summary>
internal sealed partial class WindowsProcessTokenInspector : IProcessTokenInspector
{
    private const string AdministratorsSid = "S-1-5-32-544";
    private const string PolicyKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    private const int TokenElevationTypeClass = 18;
    private const int TokenIntegrityLevelClass = 25;

    public ProcessTokenInfo Inspect()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new ProcessTokenInfo(GetElevationType(identity), GetAdministratorsGroup(identity), GetAdminApprovalMode(), GetIntegrityLevel(identity));
    }

    private static unsafe int? GetIntegrityLevel(WindowsIdentity identity)
    {
        // TOKEN_MANDATORY_LABEL starts with a pointer to the label SID (S-1-16-<rid>) stored in the same buffer.
        var buffer = new byte[256];
        fixed (byte* pointer = buffer)
        {
            if (!GetTokenInformation(identity.AccessToken.DangerousGetHandle(), TokenIntegrityLevelClass, (nint)pointer, buffer.Length, out _))
                return null;

            var value = new SecurityIdentifier(*(nint*)pointer).Value;
            return int.TryParse(value.AsSpan(value.LastIndexOf('-') + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var rid) ? rid : null;
        }
    }

    private static TokenElevationKind GetElevationType(WindowsIdentity identity)
    {
        var ok = GetTokenInformation(identity.AccessToken.DangerousGetHandle(), TokenElevationTypeClass, out var value, sizeof(int), out _);
        return ok && Enum.IsDefined((TokenElevationKind)value) ? (TokenElevationKind)value : TokenElevationKind.Unknown;
    }

    private static AdministratorsGroupState GetAdministratorsGroup(WindowsIdentity identity)
    {
        if (identity.HasClaim(ClaimTypes.GroupSid, AdministratorsSid))
            return AdministratorsGroupState.Enabled;

        return identity.HasClaim(ClaimTypes.DenyOnlySid, AdministratorsSid)
            ? AdministratorsGroupState.DenyOnly
            : AdministratorsGroupState.Missing;
    }

    private static int? GetAdminApprovalMode()
    {
        using var key = Registry.LocalMachine.OpenSubKey(PolicyKey);
        return key?.GetValue("TypeOfAdminApprovalMode") as int?;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, out int tokenInformation, int tokenInformationLength, out int returnLength);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, nint tokenInformation, int tokenInformationLength, out int returnLength);
}
