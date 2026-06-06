# Builds the PCM Hammer installer locally.
#
# It stages each app's build output (plus the kernels) into a temporary folder in
# the layout the .iss expects, then runs the Inno Setup compiler (ISCC) to produce
# Apps\installer\output\setup.exe.
#
# Prerequisites:
#   - The apps are already built (default Release). Build them first, e.g.:
#       msbuild Apps\UI\WindowsForms\PcmHammer\PcmHammer.csproj   /p:Configuration=Release
#       msbuild Apps\UI\WindowsForms\PcmLogger\PcmLogger.csproj   /p:Configuration=Release
#       msbuild Apps\UI\WindowsForms\VpwExplorer\VpwExplorer.csproj /p:Configuration=Release
#       msbuild Apps\UI\PcmHammerCLI\PcmHammerCLI.csproj          /p:Configuration=Release
#   - The kernels are built into Kernels\build\*.bin
#   - Inno Setup 6 is installed (ISCC.exe), or pass -IsccPath.
#
# Usage:
#   pwsh Apps\installer\build-installer.ps1 -Version 2026.06.06
#   pwsh Apps\installer\build-installer.ps1 -Configuration Release -Version 2026.06.06

[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Version = "0.0.0",
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"

$installerDir = $PSScriptRoot
$repoRoot     = (Resolve-Path (Join-Path $installerDir "..\..")).Path
$appsDir      = Join-Path $repoRoot "Apps"
$kernelDir    = Join-Path $repoRoot "Kernels\build"
$staging      = Join-Path $installerDir "staging"

function Copy-AppOutput {
    param([string]$ProjectBin, [string]$ExeName, [string]$DestName)
    $exe = Get-ChildItem -Path $ProjectBin -Recurse -File -Filter $ExeName -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like "*\$Configuration\*" } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $exe) { throw "Could not find $ExeName under $ProjectBin ($Configuration). Build it first." }
    $dest = Join-Path $staging "WinForms\$DestName"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item -Path (Join-Path $exe.Directory.FullName "*") -Destination $dest -Recurse -Force
    Write-Host "Staged $DestName from $($exe.Directory.FullName)"
}

# Clean staging
if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
New-Item -ItemType Directory -Force -Path $staging | Out-Null

# GUI apps
Copy-AppOutput -ProjectBin (Join-Path $appsDir "UI\WindowsForms\PcmHammer\bin")   -ExeName "PcmHammer.exe"   -DestName "PcmHammer"
Copy-AppOutput -ProjectBin (Join-Path $appsDir "UI\WindowsForms\PcmLogger\bin")   -ExeName "PcmLogger.exe"   -DestName "PcmLogger"
Copy-AppOutput -ProjectBin (Join-Path $appsDir "UI\WindowsForms\VpwExplorer\bin") -ExeName "VpwExplorer.exe" -DestName "VpwExplorer"

# Logger profiles / parameter files (read-only resources next to PcmLogger.exe)
Get-ChildItem -Path (Join-Path $appsDir "UI\WindowsForms\PcmLogger") -Filter "*.LogProfile" -ErrorAction SilentlyContinue |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $staging "WinForms\PcmLogger") -Force }
Get-ChildItem -Path (Join-Path $appsDir "UI\WindowsForms\PcmLogger") -Filter "Parameters.*.xml" -ErrorAction SilentlyContinue |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $staging "WinForms\PcmLogger") -Force }

# Kernels next to PcmHammer (GUI loads them from its exe dir)
if (-not (Test-Path $kernelDir)) { throw "Kernel build dir not found: $kernelDir. Build the kernels first." }
Copy-Item -Path (Join-Path $kernelDir "*.bin") -Destination (Join-Path $staging "WinForms\PcmHammer") -Force

# CLI
$cli = Get-ChildItem -Path (Join-Path $appsDir "UI\PcmHammerCLI\bin") -Recurse -File -Filter "pcmhammer-cli.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -like "*\$Configuration\*" } |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $cli) { throw "Could not find pcmhammer-cli.exe ($Configuration). Build it first." }
New-Item -ItemType Directory -Force -Path (Join-Path $staging "CLI") | Out-Null
Copy-Item $cli.FullName (Join-Path $staging "CLI") -Force

# Locate ISCC
if (-not $IsccPath) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
    )
    $IsccPath = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $IsccPath -or -not (Test-Path $IsccPath)) {
    throw "ISCC.exe not found. Install Inno Setup 6 or pass -IsccPath."
}

# Compile
& $IsccPath `
    "/DAppVersion=$Version" `
    "/DWinFormsRoot=$(Join-Path $staging 'WinForms')" `
    "/DCliRoot=$(Join-Path $staging 'CLI')" `
    (Join-Path $installerDir "pcmhammer-setup.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

Write-Host "Installer built: $(Join-Path $installerDir 'output\setup.exe')"
