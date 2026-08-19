<#
.SYNOPSIS
    Verwirft die Podman-Machine und setzt sie rootless neu auf, inkl. Registry-Konfiguration.

.DESCRIPTION
    Ablauf:
      1. Vorhandene Machine stoppen und entfernen (optional inkl. WSL-Distro-Reste)
      2. Neu initialisieren mit --rootful=false
      3. Registry-Konfiguration im HOME des Machine-Users ablegen
      4. Machine neu starten und Ergebnis verifizieren

    Zur Ablage-Logik (containers/image, sysregistriesv2):
      Existiert ~/.config/containers/registries.conf, werden /etc/containers/registries.conf
      UND /etc/containers/registries.conf.d KOMPLETT ignoriert. Gelesen wird dann nur noch
      die User-Datei plus ~/.config/containers/registries.conf.d/*.conf.
      Deshalb landet hier alles im User-Kontext - Mischen der beiden Ebenen funktioniert nicht.

.EXAMPLE
    .\Reset-PodmanMachine.ps1 -InsecureRegistries '10.24.9.62:2375' `
                              -SearchRegistries '10.24.9.62:2375','docker.io' -Force
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]   $MachineName        = 'podman-machine-default',
    [int]      $Cpus               = 4,
    [int]      $MemoryMB           = 4096,
    [int]      $DiskSizeGB         = 100,

    # Registries per HTTP bzw. mit ungueltigem Zertifikat
    [string[]] $InsecureRegistries = @('10.24.9.62:2375'),

    # Reihenfolge fuer Short-Form-Pulls. Reihenfolge = Suchreihenfolge!
    [string[]] $SearchRegistries   = @('10.24.9.62:2375', 'docker.io'),

    [ValidateSet('enforcing', 'permissive', 'disabled')]
    [string]   $ShortNameMode      = 'permissive',

    # Optionale Mirrors: @{ 'docker.io' = '10.24.9.62:2375' }
    [hashtable] $RegistryMirrors   = @{},

    # Kopiert die System-Short-Name-Aliase (ubi9, fedora, ...) in den User-Kontext,
    # da /etc/containers/registries.conf.d sonst wegfaellt.
    [switch]   $PreserveShortNameAliases,

    [string[]] $Volumes            = @(),
    [switch]   $UserModeNetworking,
    [switch]   $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Pfade im HOME des Machine-Users ('user' bei WSL, 'core' bei HyperV/applehv).
# Tilde wird in der Remote-Shell aufgeloest, daher providerunabhaengig.
$ConfBase   = '~/.config/containers/registries.conf'
$ConfDir    = '~/.config/containers/registries.conf.d'
$ConfSearch = "$ConfDir/zzz-search.conf"

#region Helpers ---------------------------------------------------------------

function Invoke-Podman {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [switch] $IgnoreExitCode
    )

    Write-Verbose "podman $($Arguments -join ' ')"
    $output = & podman @Arguments 2>&1 | ForEach-Object { "$_" }
    $code   = $LASTEXITCODE

    if ($code -ne 0 -and -not $IgnoreExitCode) {
        throw "podman $($Arguments -join ' ') schlug fehl (Exit $code):`n$($output -join [Environment]::NewLine)"
    }

    [pscustomobject]@{
        ExitCode = $code
        Output   = ($output -join [Environment]::NewLine)
    }
}

function Invoke-InMachine {
    param(
        [Parameter(Mandatory)][string] $Machine,
        [Parameter(Mandatory)][string] $Command,
        [switch] $IgnoreExitCode
    )
    Invoke-Podman -Arguments @('machine', 'ssh', $Machine, $Command) -IgnoreExitCode:$IgnoreExitCode
}

function Test-MachineExists {
    param([string] $Name)
    (Invoke-Podman -Arguments @('machine', 'inspect', $Name) -IgnoreExitCode).ExitCode -eq 0
}

function Write-FileToMachine {
    <#
        Schreibt Text in eine Datei in der VM. base64 vermeidet Quoting-Probleme
        zwischen PowerShell, ssh und der Remote-Shell und haelt die Zeilenenden bei LF.
        Ohne sudo: die Dateien gehoeren dem rootless-User, der auch den podman-Service faehrt.
    #>
    param(
        [Parameter(Mandatory)][string] $Machine,
        [Parameter(Mandatory)][string] $TargetPath,
        [Parameter(Mandatory)][string] $Content
    )

    $normalized = $Content -replace "`r`n", "`n"
    $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($normalized))
    $dir = $TargetPath.Substring(0, $TargetPath.LastIndexOf('/'))

    Invoke-InMachine -Machine $Machine `
        -Command "mkdir -p $dir && echo '$b64' | base64 -d > $TargetPath && chmod 0644 $TargetPath" | Out-Null
}

function New-BaseConf {
    param([string[]] $Insecure, [hashtable] $Mirrors)

    $sb = [Text.StringBuilder]::new()
    [void]$sb.AppendLine('# Erzeugt von Reset-PodmanMachine.ps1 - Aenderungen werden ueberschrieben.')
    [void]$sb.AppendLine('# ACHTUNG: Diese Datei deaktiviert /etc/containers/registries.conf{,.d} vollstaendig.')
    [void]$sb.AppendLine("# Stand: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
    [void]$sb.AppendLine('')

    foreach ($reg in $Insecure) {
        [void]$sb.AppendLine('[[registry]]')
        [void]$sb.AppendLine("location = `"$reg`"")
        [void]$sb.AppendLine('insecure = true')
        [void]$sb.AppendLine('')
    }

    foreach ($src in $Mirrors.Keys) {
        $dst = $Mirrors[$src]
        [void]$sb.AppendLine('[[registry]]')
        [void]$sb.AppendLine("location = `"$src`"")
        [void]$sb.AppendLine('')
        [void]$sb.AppendLine('[[registry.mirror]]')
        [void]$sb.AppendLine("location = `"$dst`"")
        if ($Insecure -contains $dst) { [void]$sb.AppendLine('insecure = true') }
        [void]$sb.AppendLine('')
    }

    $sb.ToString()
}

function New-SearchConf {
    param([string[]] $Search, [string] $Mode)

    $list = ($Search | ForEach-Object { '"{0}"' -f $_ }) -join ', '
    @"
# Erzeugt von Reset-PodmanMachine.ps1 - Aenderungen werden ueberschrieben.
unqualified-search-registries = [$list]
short-name-mode = "$Mode"
"@
}

#endregion --------------------------------------------------------------------

#region 0. Vorbedingungen -----------------------------------------------------

if (-not (Get-Command podman -ErrorAction SilentlyContinue)) {
    throw 'podman.exe wurde nicht im PATH gefunden.'
}

Write-Host "Podman-Version: $((Invoke-Podman -Arguments @('--version')).Output)" -ForegroundColor DarkGray

if (-not $Force -and -not $PSCmdlet.ShouldProcess($MachineName, 'Machine loeschen und neu aufsetzen')) {
    Write-Host 'Abgebrochen.' -ForegroundColor Yellow
    return
}

#endregion

#region 1. Alte Machine entfernen ---------------------------------------------

if (Test-MachineExists -Name $MachineName) {
    Write-Host "-> Stoppe '$MachineName' ..." -ForegroundColor Cyan
    Invoke-Podman -Arguments @('machine', 'stop', $MachineName) -IgnoreExitCode | Out-Null

    Write-Host "-> Entferne '$MachineName' ..." -ForegroundColor Cyan
    Invoke-Podman -Arguments @('machine', 'rm', '--force', $MachineName) | Out-Null
} else {
    Write-Host "-> Keine bestehende Machine '$MachineName' gefunden." -ForegroundColor DarkGray
}

# Sicherheitsnetz: verwaiste WSL-Distro blockiert sonst das naechste init
$wsl = Get-Command wsl.exe -ErrorAction SilentlyContinue
if ($wsl) {
    $distros = (& wsl.exe --list --quiet) -replace "`0", '' | ForEach-Object { $_.Trim() }
    if ($distros -contains $MachineName) {
        Write-Warning "WSL-Distro '$MachineName' existiert noch - wird abgemeldet."
        & wsl.exe --unregister $MachineName | Out-Null
    }
}

#endregion

#region 2. Neu initialisieren (rootless) --------------------------------------

$initArgs = @(
    'machine', 'init', $MachineName,
    '--rootful=false',
    "--cpus=$Cpus",
    "--memory=$MemoryMB",
    "--disk-size=$DiskSizeGB",
    '--now'
)
if ($UserModeNetworking) { $initArgs += '--user-mode-networking' }
foreach ($v in $Volumes)  { $initArgs += @('--volume', $v) }

Write-Host "-> Initialisiere '$MachineName' (rootless) ..." -ForegroundColor Cyan
Invoke-Podman -Arguments $initArgs | Out-Null

Invoke-Podman -Arguments @('system', 'connection', 'default', $MachineName) -IgnoreExitCode | Out-Null

Write-Host '-> Warte auf Bereitschaft ...' -ForegroundColor Cyan
$ready = $false
foreach ($i in 1..30) {
    if ((Invoke-InMachine -Machine $MachineName -Command 'true' -IgnoreExitCode).ExitCode -eq 0) {
        $ready = $true; break
    }
    Start-Sleep -Seconds 2
}
if (-not $ready) { throw "Machine '$MachineName' wurde nicht rechtzeitig erreichbar." }

#endregion

#region 3. Konfiguration im User-Kontext schreiben ----------------------------

# Vorher festhalten, was durch die User-Datei ausgeblendet wird
$systemDropIns = (Invoke-InMachine -Machine $MachineName `
    -Command 'ls -1 /etc/containers/registries.conf.d/ 2>/dev/null' -IgnoreExitCode).Output

Write-Host "-> Schreibe $ConfBase ..." -ForegroundColor Cyan
Write-FileToMachine -Machine $MachineName -TargetPath $ConfBase `
                    -Content (New-BaseConf -Insecure $InsecureRegistries -Mirrors $RegistryMirrors)

Write-Host "-> Schreibe $ConfSearch ..." -ForegroundColor Cyan
Write-FileToMachine -Machine $MachineName -TargetPath $ConfSearch `
                    -Content (New-SearchConf -Search $SearchRegistries -Mode $ShortNameMode)

if ($PreserveShortNameAliases) {
    Write-Host '-> Uebernehme System-Short-Name-Aliase ...' -ForegroundColor Cyan
    # 000-* wird lexikalisch vor zzz-search.conf gelesen, enthaelt nur [aliases] -> kein Konflikt
    Invoke-InMachine -Machine $MachineName -IgnoreExitCode -Command @"
for f in /etc/containers/registries.conf.d/*shortnames*.conf; do
  [ -e "`$f" ] && cp -n "`$f" $ConfDir/ ;
done
"@ | Out-Null
}

#endregion

#region 4. Neustart und Verifikation ------------------------------------------

Write-Host '-> Starte Machine neu ...' -ForegroundColor Cyan
Invoke-Podman -Arguments @('machine', 'stop',  $MachineName) -IgnoreExitCode | Out-Null
Invoke-Podman -Arguments @('machine', 'start', $MachineName) | Out-Null

$rootful  = (Invoke-Podman -Arguments @('machine', 'inspect', '--format', '{{.Rootful}}', $MachineName)).Output.Trim()
$search   = (Invoke-Podman -Arguments @('info', '--format', '{{.Registries.search}}')).Output.Trim()
$effectiv = (Invoke-InMachine -Machine $MachineName -IgnoreExitCode `
                -Command "cat $ConfBase $ConfSearch").Output

Write-Host ''
Write-Host '=== Ergebnis ===' -ForegroundColor Green
Write-Host "Machine        : $MachineName"
Write-Host "Rootful        : $rootful  (erwartet: false)"
Write-Host "Search-Registry: $search"
Write-Host '--- effektive User-Konfiguration ---' -ForegroundColor DarkGray
Write-Host $effectiv -ForegroundColor DarkGray

if ($rootful -ne 'false') {
    Write-Warning "Machine laeuft nicht rootless. Korrektur: podman machine set --rootful=false $MachineName"
}

if ($systemDropIns -and -not $PreserveShortNameAliases) {
    Write-Warning "Folgende System-Drop-ins werden jetzt ignoriert (User-registries.conf hat Vorrang):`n$systemDropIns"
    Write-Warning 'Bei Bedarf -PreserveShortNameAliases verwenden.'
}

#endregion