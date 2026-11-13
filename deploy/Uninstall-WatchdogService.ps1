#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Entfernt den MSEM001 Watchdog-Dienst. Ein laufender Jenkins-Agent bleibt verbunden.

.EXAMPLE
    .\Uninstall-WatchdogService.ps1 -RemoveFiles
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string] $ServiceName = 'Msem001Watchdog',
    [string] $InstallPath = 'C:\Services\Msem001Watchdog',
    [switch] $RemoveFiles
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Host "Dienst $ServiceName ist nicht installiert." -ForegroundColor DarkGray
}
elseif ($PSCmdlet.ShouldProcess($ServiceName, 'Dienst stoppen und entfernen')) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $ServiceName -Force
        $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
    }

    & sc.exe delete $ServiceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sc.exe delete schlug fehl (Exit $LASTEXITCODE)." }
    Write-Host "Dienst $ServiceName entfernt." -ForegroundColor Green
}

if ($RemoveFiles -and (Test-Path $InstallPath) -and $PSCmdlet.ShouldProcess($InstallPath, 'Installationsverzeichnis loeschen')) {
    Remove-Item -Path $InstallPath -Recurse -Force
    Write-Host "$InstallPath geloescht (Logs unter $env:ProgramData\Msem001Watchdog bleiben erhalten)." -ForegroundColor Green
}
