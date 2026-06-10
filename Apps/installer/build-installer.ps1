# Compiles the PCM Hammer installer (Inno Setup) from a pre-staged payload.
#
# The staging layout is produced by Apps\build\Build-Apps.ps1 and looks like:
#   <StagingRoot>\WinForms\PcmHammer\   (PcmHammer.exe + DLLs + kernels)
#   <StagingRoot>\WinForms\PcmLogger\
#   <StagingRoot>\WinForms\VpwExplorer\
#   <StagingRoot>\CLI\pcmhammer-cli.exe
#
# Usage (normally called by Apps\build\Build-All.ps1):
#   pwsh Apps\installer\build-installer.ps1 -StagingRoot <repo>\dist\staging `
#        -Version 1.0.1.0 -SetupName PCMHammer_1.0.1.0 -OutputDir <repo>\dist

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$StagingRoot,
    [string]$Version = "0.0.0",
    [string]$SetupName = "setup",
    [string]$OutputDir,
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"

$installerDir = $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $installerDir "output" }

$winFormsRoot = Join-Path $StagingRoot "WinForms"
$cliRoot      = Join-Path $StagingRoot "CLI"
foreach ($d in @($winFormsRoot, $cliRoot)) {
    if (-not (Test-Path $d)) { throw "Staging folder not found: $d (run Build-Apps.ps1 first)" }
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# Locate ISCC
if (-not $IsccPath) {
    $IsccPath = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path $IsccPath)) {
    throw "ISCC.exe not found. Install Inno Setup 6 or pass -IsccPath."
}

& $IsccPath `
    "/DAppVersion=$Version" `
    "/DSetupName=$SetupName" `
    "/DOutDir=$( (Resolve-Path $OutputDir).Path )" `
    "/DWinFormsRoot=$( (Resolve-Path $winFormsRoot).Path )" `
    "/DCliRoot=$( (Resolve-Path $cliRoot).Path )" `
    (Join-Path $installerDir "pcmhammer-setup.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

$out = Join-Path $OutputDir "$SetupName.exe"
Write-Host "Installer built: $out"
