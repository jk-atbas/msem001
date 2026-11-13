namespace Msem001.Watchdog.Hosting;

/// <summary>
/// State of the local Administrators group (S-1-5-32-544) in the process token.
/// </summary>
internal enum AdministratorsGroupState
{
    /// <summary>The group is present and enabled: the process has administrator rights.</summary>
    Enabled,

    /// <summary>The group is present for deny checks only: the token was filtered (UAC).</summary>
    DenyOnly,

    /// <summary>The account is not a member of the local Administrators group.</summary>
    Missing,
}
