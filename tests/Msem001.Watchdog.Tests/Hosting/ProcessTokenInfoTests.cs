using Msem001.Watchdog.Hosting;

namespace Msem001.Watchdog.Tests.Hosting;

public sealed class ProcessTokenInfoTests
{
    [Test]
    [Arguments((int)AdministratorsGroupState.Missing)]
    [Arguments((int)AdministratorsGroupState.DenyOnly)]
    [Arguments((int)AdministratorsGroupState.Enabled)]
    public async Task Explain_AdministratorProtection_TakesPrecedence(int group)
    {
        var info = new ProcessTokenInfo(TokenElevationKind.Default, (AdministratorsGroupState)group, ProcessTokenInfo.AdministratorProtectionMode, 0x2000);

        await Assert.That(info.Explain()).StartsWith("Windows-Administratorschutz ist aktiv").And.Contains("deaktivieren");
    }

    [Test]
    [Arguments(null)]
    [Arguments(1)]
    public async Task Explain_NotAMember_SuggestsAddingToAdministrators(int? approvalMode)
    {
        var info = new ProcessTokenInfo(TokenElevationKind.Default, AdministratorsGroupState.Missing, approvalMode, 0x2000);

        await Assert.That(info.Explain()).Contains("nicht Mitglied").And.Contains("Add-LocalGroupMember -SID S-1-5-32-544");
    }

    [Test]
    public async Task Explain_DenyOnly_ReportsUacFilteredToken()
    {
        var info = new ProcessTokenInfo(TokenElevationKind.Limited, AdministratorsGroupState.DenyOnly, 1, 0x2000);

        await Assert.That(info.Explain()).Contains("UAC-gefiltert").And.Contains("Elevation Limited");
    }

    [Test]
    public async Task Explain_EnabledButMediumIntegrity_ReportsIntegrityLevel()
    {
        var info = new ProcessTokenInfo(TokenElevationKind.Default, AdministratorsGroupState.Enabled, 1, 0x2000);

        await Assert.That(info.Explain()).Contains("Integritätsstufe ist aber nur Medium (High erforderlich, Elevation Default)");
    }

    [Test]
    [Arguments((int)AdministratorsGroupState.Enabled, 0x3000, true)]
    [Arguments((int)AdministratorsGroupState.Enabled, 0x4000, true)]
    [Arguments((int)AdministratorsGroupState.Enabled, null, true)]
    [Arguments((int)AdministratorsGroupState.Enabled, 0x2000, false)]
    [Arguments((int)AdministratorsGroupState.DenyOnly, 0x3000, false)]
    [Arguments((int)AdministratorsGroupState.Missing, 0x4000, false)]
    public async Task HasAdministratorRights_RequiresEnabledGroupAndHighIntegrity(int group, int? integrity, bool expected)
    {
        var info = new ProcessTokenInfo(TokenElevationKind.Default, (AdministratorsGroupState)group, 1, integrity);

        await Assert.That(info.HasAdministratorRights).IsEqualTo(expected);
    }

    [Test]
    [Arguments(null, "unbekannt")]
    [Arguments(0x1000, "Low")]
    [Arguments(0x2000, "Medium")]
    [Arguments(0x2100, "Medium Plus")]
    [Arguments(0x3000, "High")]
    [Arguments(0x4000, "System")]
    [Arguments(0x5000, "0x5000")]
    public async Task IntegrityLevelName_MapsKnownLevels(int? integrity, string expected)
    {
        var info = new ProcessTokenInfo(TokenElevationKind.Default, AdministratorsGroupState.Enabled, null, integrity);

        await Assert.That(info.IntegrityLevelName).IsEqualTo(expected);
    }

    [Test]
    [Category("Integration")]
    public async Task WindowsProcessTokenInspector_ReadsCurrentToken()
    {
        var info = new WindowsProcessTokenInspector().Inspect();

        await Assert.That(info.ElevationType).IsNotEqualTo(TokenElevationKind.Unknown);
        await Assert.That(info.IntegrityLevel).IsNotNull();

        // Interactive session: elevated <=> Administrators group enabled and High integrity.
        await Assert.That(info.HasAdministratorRights).IsEqualTo(SystemEnvironmentProvider.Instance.IsPrivilegedProcess);
        var expectedGroup = SystemEnvironmentProvider.Instance.IsPrivilegedProcess
            ? AdministratorsGroupState.Enabled
            : AdministratorsGroupState.DenyOnly;
        if (info.AdministratorsGroup != AdministratorsGroupState.Missing)
            await Assert.That(info.AdministratorsGroup).IsEqualTo(expectedGroup);
    }
}
