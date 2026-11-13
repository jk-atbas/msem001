namespace Msem001.Watchdog.Hosting;

/// <summary>
/// Elevation type of the process token (<c>TOKEN_ELEVATION_TYPE</c>).
/// </summary>
internal enum TokenElevationKind
{
    /// <summary>The elevation type could not be determined.</summary>
    Unknown = 0,

    /// <summary>No linked token: UAC is off, or the account is not an administrator, or administrator protection is active.</summary>
    Default = 1,

    /// <summary>Full administrator token.</summary>
    Full = 2,

    /// <summary>UAC-filtered token of an administrator.</summary>
    Limited = 3,
}
