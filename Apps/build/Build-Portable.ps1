# Assembles the portable zip from a staged build.
#
# Produces <OutputDir>\<OutputName>.zip containing a single top-level PCMHammer\ folder:
#   PCMHammer\PcmHammer\    PcmHammer.exe + DLLs + kernels
#   PCMHammer\PcmLogger\
#   PCMHammer\VpwExplorer\
#   PCMHammer\Cli\          pcmhammer-cli.exe + kernels
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

# Build the PCMHammer\ tree under a temp folder.
$tmp  = Join-Path $OutputDir "portable_tmp"
$root = Join-Path $tmp "PCMHammer"
if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp }
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

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$zip = Join-Path $OutputDir "$OutputName.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $root -DestinationPath $zip -Force

Remove-Item -Recurse -Force $tmp
Write-Host "Portable built: $zip"
