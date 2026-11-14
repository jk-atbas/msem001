#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installiert bzw. aktualisiert den MSEM001 Watchdog als Windows-Dienst unter dem lokalen Admin-Konto.

.DESCRIPTION
    Ablauf:
      1. Laufenden Dienst stoppen. Der Dienst beendet dabei den Jenkins-Agent, laufende Builds werden abgebrochen
         (ausser Watchdog:JenkinsAgent:StopWithService = false). Updates daher ohne laufende Builds einspielen
         oder den Node vorher in Jenkins offline schalten. Nach Schritt 6 verbindet der Dienst den Agent neu.
      2. Dateien nach -InstallPath spiegeln (appsettings.Local.json und Logs bleiben erhalten)
      3. Dem Dienstkonto das Recht "Als Dienst anmelden" (SeServiceLogonRight) gewaehren
      4. Dienst anlegen bzw. Konto/Pfad aktualisieren
      5. Start "Automatisch (Verzoegerter Start)" und Neustart bei Absturz konfigurieren
      6. Dienst starten

    Der Dienst laeuft unter dem Konto, unter dem auch die Podman-Machine eingerichtet ist (MSEM001\atbas).
    Der vom Dienst gestartete Jenkins-Agent erbt dieses Konto. Dienst-Anmeldungen unterliegen nicht der
    UAC-Filterung, der Agent laeuft daher mit vollem Administrator-Token.

.EXAMPLE
    .\Install-WatchdogService.ps1 -SourcePath ..\publish

.EXAMPLE
    .\Install-WatchdogService.ps1 -SourcePath C:\temp\publish -Credential (Get-Credential 'MSEM001\atbas')
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory)]
    [string] $SourcePath,

    [string] $InstallPath = 'C:\Services\Msem001Watchdog',

    [string] $ServiceName = 'Msem001Watchdog',

    [pscredential] $Credential
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ExeName     = 'Msem001.Watchdog.exe'
$DisplayName = 'MSEM001 Build-Agent Watchdog'
$Description = 'Ueberwacht Speicherplatz, Podman-Machine/win-sshproxy und den Jenkins-Agent auf MSEM001 und meldet Fehler per Mail.'

#region Helpers ---------------------------------------------------------------

function Grant-ServiceLogonRight {
    param([Parameter(Mandatory)][string] $Account)

    $sid = ([Security.Principal.NTAccount] $Account).Translate([Security.Principal.SecurityIdentifier]).Value
    $tempBase = Join-Path $env:TEMP "msem001-secpol-$([guid]::NewGuid().ToString('N'))"
    $export = "$tempBase-export.inf"
    $import = "$tempBase-import.inf"
    $db     = "$tempBase.sdb"

    try {
        & secedit.exe /export /cfg $export /areas USER_RIGHTS | Out-Null
        $line = Get-Content $export | Where-Object { $_ -like 'SeServiceLogonRight*' } | Select-Object -First 1

        if ($line -and ($line -match "\*$sid(,|$)")) {
            Write-Host "-> $Account besitzt bereits 'Als Dienst anmelden'." -ForegroundColor DarkGray
            return
        }

        $newLine = if ($line) { "$line,*$sid" } else { "SeServiceLogonRight = *$sid" }
        @(
            '[Unicode]'
            'Unicode=yes'
            '[Version]'
            'signature="$CHICAGO$"'
            'Revision=1'
            '[Privilege Rights]'
            $newLine
        ) | Set-Content -Path $import -Encoding Unicode

        Write-Host "-> Gewaehre $Account 'Als Dienst anmelden' ..." -ForegroundColor Cyan
        & secedit.exe /configure /db $db /cfg $import /areas USER_RIGHTS | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "secedit /configure schlug fehl (Exit $LASTEXITCODE)." }
    }
    finally {
        Remove-Item $export, $import, $db -ErrorAction SilentlyContinue
    }
}

function Invoke-Sc {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $output = & sc.exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "sc.exe $($Arguments -join ' ') schlug fehl (Exit $LASTEXITCODE): $output" }
}

#endregion --------------------------------------------------------------------

$source = Resolve-Path $SourcePath
if (-not (Test-Path (Join-Path $source $ExeName))) {
    throw "$ExeName nicht in $source gefunden. Zuerst deploy\Publish-Watchdog.ps1 ausfuehren."
}

if (-not $Credential) {
    $Credential = Get-Credential -UserName "$env:COMPUTERNAME\atbas" -Message 'Dienstkonto (lokaler Administrator)'
}
# Lokale Konten als MASCHINE\name, das verstehen NTAccount, icacls und der Dienst-Manager gleichermassen
$account = $Credential.UserName -replace '^\.\\', ''
if ($account -notmatch '\\') { $account = "$env:COMPUTERNAME\$account" }

if (-not $PSCmdlet.ShouldProcess($ServiceName, "Installieren nach $InstallPath als $account")) { return }

#region 1. Dienst stoppen ------------------------------------------------------

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    Write-Host "-> Stoppe $ServiceName (beendet auch den Jenkins-Agent, laufende Builds brechen ab) ..." -ForegroundColor Cyan
    Stop-Service -Name $ServiceName -Force
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}

#endregion

#region 2. Dateien kopieren ----------------------------------------------------

Write-Host "-> Kopiere Dateien nach $InstallPath ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
& robocopy.exe $source $InstallPath /MIR /NFL /NDL /NJH /NJS /NP /XF appsettings.Local.json /XD logs | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy schlug fehl (Exit $LASTEXITCODE)." }

$localSettings = Join-Path $InstallPath 'appsettings.Local.json'
if (-not (Test-Path $localSettings)) {
    Copy-Item (Join-Path $InstallPath 'appsettings.Local.example.json') $localSettings
    Write-Warning "$localSettings wurde aus der Vorlage angelegt. Jenkins-Secret, SMTP-Zugangsdaten und Empfaenger eintragen, dann den Dienst starten."
}

# Secrets nur fuer Administratoren und SYSTEM lesbar machen
& icacls.exe $localSettings /inheritance:r /grant:r '*S-1-5-32-544:F' '*S-1-5-18:F' "$($account):R" | Out-Null

#endregion

#region 3. Anmelderecht --------------------------------------------------------

Grant-ServiceLogonRight -Account $account

#endregion

#region 4. Dienst anlegen/aktualisieren ---------------------------------------

$binaryPath = '"{0}"' -f (Join-Path $InstallPath $ExeName)
$password   = $Credential.GetNetworkCredential().Password

if (-not $service) {
    Write-Host "-> Lege Dienst $ServiceName an ..." -ForegroundColor Cyan
    New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName $DisplayName `
                -Description $Description -StartupType Automatic -Credential $Credential | Out-Null
}
else {
    Write-Host "-> Aktualisiere Dienst $ServiceName ..." -ForegroundColor Cyan
    $cim = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'"
    $result = Invoke-CimMethod -InputObject $cim -MethodName Change -Arguments @{
        PathName      = $binaryPath
        StartName     = $account
        StartPassword = $password
        DisplayName   = $DisplayName
    }
    if ($result.ReturnValue -ne 0) { throw "Win32_Service.Change schlug fehl (ReturnValue $($result.ReturnValue))." }
    Invoke-Sc -Arguments @('description', $ServiceName, $Description)
}

#endregion

#region 5. Start- und Wiederherstellungsoptionen -------------------------------

# Verzoegerter Start: Netzwerk, WSL und Podman-Abhaengigkeiten sind nach dem Boot bereit.
Invoke-Sc -Arguments @('config', $ServiceName, 'start=', 'delayed-auto')
# Bei Absturz nach 1 min, 1 min, 5 min neu starten; Fehlerzaehler nach 1 Tag zuruecksetzen.
Invoke-Sc -Arguments @('failure', $ServiceName, 'reset=', '86400', 'actions=', 'restart/60000/restart/60000/restart/300000')
# Auch bei Beenden mit Fehlercode (z.B. ungueltige Konfiguration) die Wiederherstellung ausloesen.
Invoke-Sc -Arguments @('failureflag', $ServiceName, '1')

#endregion

#region 6. Starten -------------------------------------------------------------

Write-Host "-> Starte $ServiceName ..." -ForegroundColor Cyan
Start-Service -Name $ServiceName
(Get-Service -Name $ServiceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))

Write-Host ''
Write-Host '=== Ergebnis ===' -ForegroundColor Green
Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'" |
    Select-Object Name, State, StartMode, StartName, PathName | Format-List
Write-Host "Logs: $env:ProgramData\Msem001Watchdog\logs" -ForegroundColor DarkGray

#endregion
