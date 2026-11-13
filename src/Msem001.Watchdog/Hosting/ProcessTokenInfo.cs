namespace Msem001.Watchdog.Hosting;

/// <summary>
/// Facts about the process token that explain a missing elevation.
/// </summary>
/// <param name="ElevationType">Elevation type of the token.</param>
/// <param name="AdministratorsGroup">State of the Administrators group in the token.</param>
/// <param name="AdminApprovalMode">Registry value <c>TypeOfAdminApprovalMode</c> (2 = administrator protection), <see langword="null"/> if not set.</param>
/// <param name="IntegrityLevel">Mandatory integrity level RID of the token (e.g. 0x3000 = High), <see langword="null"/> if unknown.</param>
/// <remarks>
/// <c>TokenIsElevated</c> (and therefore <c>Environment.IsPrivilegedProcess</c>) is <see langword="false"/> for an administrator
/// running as a service without a UAC split token, although the token carries the enabled Administrators group and a high
/// integrity level. <see cref="HasAdministratorRights"/> therefore looks at the token contents.
/// </remarks>
internal sealed record ProcessTokenInfo(
    TokenElevationKind ElevationType,
    AdministratorsGroupState AdministratorsGroup,
    int? AdminApprovalMode,
    int? IntegrityLevel)
{
    /// <summary>Value of <c>TypeOfAdminApprovalMode</c> when Windows administrator protection is enabled.</summary>
    public const int AdministratorProtectionMode = 2;

    /// <summary>Integrity level RID <c>SECURITY_MANDATORY_HIGH_RID</c>.</summary>
    public const int HighIntegrity = 0x3000;

    /// <summary>
    /// Gets a value indicating whether the process can act as administrator: the Administrators group is enabled
    /// and the integrity level is at least High (or unknown).
    /// </summary>
    public bool HasAdministratorRights =>
        AdministratorsGroup == AdministratorsGroupState.Enabled && (IntegrityLevel is null || IntegrityLevel >= HighIntegrity);

    /// <summary>
    /// Gets the integrity level as readable name (Low, Medium, High, System or the hex RID).
    /// </summary>
    public string IntegrityLevelName => IntegrityLevel switch
    {
        null => "unbekannt",
        0x1000 => "Low",
        0x2000 => "Medium",
        0x2100 => "Medium Plus",
        HighIntegrity => "High",
        0x4000 => "System",
        _ => $"0x{IntegrityLevel:X4}",
    };

    /// <summary>
    /// Gets a German explanation including the remedy, used in the log and in the alert mail.
    /// </summary>
    /// <returns>The explanation.</returns>
    public string Explain()
    {
        if (AdminApprovalMode == AdministratorProtectionMode)
        {
            return "Windows-Administratorschutz ist aktiv (TypeOfAdminApprovalMode = 2): Konten der Administratorgruppe erhalten " +
                   "auch als Dienst kein Admin-Token mehr. Abhilfe: Administratorschutz auf MSEM001 deaktivieren (Windows-Sicherheit > " +
                   "Kontoschutz > Administratorschutz bzw. Richtlinie 'Benutzerkontensteuerung: Typ des Administratorgenehmigungsmodus " +
                   "konfigurieren' = Legacy) und Windows neu starten";
        }

        return AdministratorsGroup switch
        {
            AdministratorsGroupState.Missing =>
                "Das Konto ist nicht Mitglied der lokalen Gruppe Administratoren (S-1-5-32-544). Abhilfe: " +
                "Add-LocalGroupMember -SID S-1-5-32-544 -Member <Konto>, danach Dienst neu starten",
            AdministratorsGroupState.DenyOnly =>
                "Das Token ist UAC-gefiltert (Administratorgruppe nur 'deny only', Elevation " + ElevationType + "). " +
                "Prüfen: Richtlinien unter HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System und Dienstkonfiguration (sc qsidtype, sc qprivs)",
            _ => "Administratorgruppe ist aktiv, die Integritätsstufe ist aber nur " + IntegrityLevelName + " (High erforderlich, Elevation " + ElevationType + ")",
        };
    }
}
