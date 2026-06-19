# Assembles the portable distribution from a staged build.
#
# Produces both:
#   <OutputDir>\<OutputName>\        the unzipped tree (for CI to upload as a folder)
#   <OutputDir>\<OutputName>.zip     the same tree zipped (Release asset / manual use)
# each containing a single version-stamped top-level folder (OutputName minus the
# trailing _Portable, e.g. PCMHammer_2.0.0\) so the version is clear once extracted:
#   PCMHammer_<ver>\PcmHammer\    PcmHammer.exe + DLLs + kernels
#   PCMHammer_<ver>\PcmLogger\
#   PCMHammer_<ver>\VpwExplorer\
#   PCMHammer_<ver>\Cli\          pcmhammer-cli.exe + kernels
#
# Defaults are read from <repo>\dist\build-info.json (written by Build-Apps.ps1).

[CmdletBinding()]
param(
    [string]$StagingRoot,
    [string]$OutputName,
    [string]$OutputDir
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$repoRoot  = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
$distDir   = Join-Path $repoRoot "dist"

if (-not $StagingRoot -or -not $OutputName) {
    $infoPath = Join-Path $distDir "build-info.json"
    if (-not (Test-Path $infoPath)) { throw "build-info.json not found; run Build-Apps.ps1 first or pass -StagingRoot/-OutputName." }
    $bi = Get-Content $infoPath -Raw | ConvertFrom-Json
    if (-not $StagingRoot) { $StagingRoot = $bi.StagingRoot }
    if (-not $OutputName)  { $OutputName  = "PCMHammer_$($bi.NameToken)_Portable" }
}
if (-not $OutputDir) { $OutputDir = $distDir }

$winFormsRoot = Join-Path $StagingRoot "WinForms"
$cliRoot      = Join-Path $StagingRoot "CLI"
if (-not (Test-Path $winFormsRoot)) { throw "Staging not found: $winFormsRoot" }

# Build the single, version-stamped top-level folder under a version-named tree dir. The
# folder is the unzipped portable layout and is kept (not just zipped): CI uploads it
# directly as a workflow artifact, and GitHub wraps an uploaded folder in exactly one zip -
# so the download is a single zip of PCMHammer_<ver>\, not a zip-inside-a-zip. The .zip
# produced below is the same tree, used as the GitHub Release asset and for manual use.
# The top folder is OutputName without the trailing _Portable (e.g. PCMHammer_2.0.0) so the
# version is obvious once extracted.
$treeDir    = Join-Path $OutputDir $OutputName
$folderName = $OutputName -replace '_Portable$', ''
$root       = Join-Path $treeDir $folderName
if (Test-Path $treeDir) { Remove-Item -Recurse -Force $treeDir }
New-Item -ItemType Directory -Force -Path $root | Out-Null

Copy-Item -Path (Join-Path $winFormsRoot "PcmHammer")   -Destination $root -Recurse -Force
Copy-Item -Path (Join-Path $winFormsRoot "PcmLogger")   -Destination $root -Recurse -Force
Copy-Item -Path (Join-Path $winFormsRoot "VpwExplorer") -Destination $root -Recurse -Force

# CLI + its kernels.
$cliDst = Join-Path $root "Cli"
New-Item -ItemType Directory -Force -Path $cliDst | Out-Null
Copy-Item -Path (Join-Path $cliRoot "*") -Destination $cliDst -Recurse -Force
Get-ChildItem -Path (Join-Path $winFormsRoot "PcmHammer") -Filter "Kernel-*.bin" | ForEach-Object { Copy-Item $_.FullName $cliDst -Force }
Get-ChildItem -Path (Join-Path $winFormsRoot "PcmHammer") -Filter "Loader-*.bin" -ErrorAction SilentlyContinue | ForEach-Object { Copy-Item $_.FullName $cliDst -Force }

$zip = Join-Path $OutputDir "$OutputName.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $root -DestinationPath $zip -Force

Write-Host "Portable tree: $treeDir"
Write-Host "Portable zip:  $zip"
