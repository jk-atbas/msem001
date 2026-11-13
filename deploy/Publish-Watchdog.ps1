<#
.SYNOPSIS
    Testet und veroeffentlicht den MSEM001 Watchdog als self-contained Single-File (win-x64).

.DESCRIPTION
    Ergebnis liegt in <Repo>\publish und kann mit Install-WatchdogService.ps1 auf MSEM001 installiert werden.
    Auf der Zielmaschine ist keine .NET-Runtime erforderlich.

.EXAMPLE
    .\deploy\Publish-Watchdog.ps1
#>
[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\publish'),
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$project  = Join-Path $repoRoot 'src\Msem001.Watchdog\Msem001.Watchdog.csproj'

Push-Location $repoRoot
try {
    if (-not $SkipTests) {
        Write-Host '-> Tests ...' -ForegroundColor Cyan
        dotnet test --solution Msem001.Watchdog.slnx -c Release
        if ($LASTEXITCODE -ne 0) { throw "Tests fehlgeschlagen (Exit $LASTEXITCODE)." }
    }

    Write-Host "-> Publish nach $OutputPath ..." -ForegroundColor Cyan
    dotnet publish $project -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $OutputPath
    if ($LASTEXITCODE -ne 0) { throw "Publish fehlgeschlagen (Exit $LASTEXITCODE)." }

    Write-Host "Fertig: $(Resolve-Path $OutputPath)" -ForegroundColor Green
}
finally {
    Pop-Location
}
