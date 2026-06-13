# Orchestrates a full local build: apps -> installer -> portable.
#
# Examples:
#   pwsh Apps\build\Build-All.ps1                  # development build (date-stamped names)
#   pwsh Apps\build\Build-All.ps1 -Version 1.0.1.0 # release-style build for local testing
#
# Outputs land in <repo>\dist:
#   PCMHammer_<token>.exe            (installer)
#   PCMHammer_<token>_Portable.zip   (portable)
# where <token> is the x.x.x.x version (release) or YYYYMMDD_HHMMSS (development).
#
# Uno targets are experimental and dev-only; they are not produced here yet.

[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$repoRoot  = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
$distDir   = Join-Path $repoRoot "dist"

Write-Host "=== Build-Apps ===" -ForegroundColor Cyan
& (Join-Path $scriptDir "Build-Apps.ps1") -Version $Version -Configuration $Configuration

$bi = Get-Content (Join-Path $distDir "build-info.json") -Raw | ConvertFrom-Json
# Consistent scheme: PCMHammer_<version>_<kind> so versions sort/cluster together.
# NameToken is the display version (e.g. 2.0.0) for a release, or a date stamp for dev.
$base = "PCMHammer_$($bi.NameToken)"

Write-Host "=== Build-Installer ===" -ForegroundColor Cyan
# Installer AppVersion (shown in Add/Remove Programs) uses the display version, matching CI.
& (Join-Path $repoRoot "Apps\installer\build-installer.ps1") `
    -StagingRoot $bi.StagingRoot -Version $bi.Display -SetupName "$($base)_Setup" -OutputDir $distDir

Write-Host "=== Build-Portable ===" -ForegroundColor Cyan
& (Join-Path $scriptDir "Build-Portable.ps1") `
    -StagingRoot $bi.StagingRoot -OutputName "$($base)_Portable" -OutputDir $distDir

Write-Host ""
Write-Host "Done. Artifacts in $distDir :" -ForegroundColor Green
Get-ChildItem $distDir -File | Where-Object { $_.Name -like "PCMHammer_*" } | ForEach-Object {
    "{0,-45} {1,10:N0} bytes" -f $_.Name, $_.Length | Write-Host
}
