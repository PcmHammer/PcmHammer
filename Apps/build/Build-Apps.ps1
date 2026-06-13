# Builds the WinForms apps + CLI in Release (or chosen config) and stages their output
# (plus kernels) into <repo>\dist\staging, ready for the installer and portable packagers.
#
# For release builds (-Version x.x.x.x, or an x.x.x.x tag on HEAD) the version is stamped
# into the apps' AssemblyInfo.cs for the duration of the build and restored afterwards,
# so the working tree is left unchanged but the compiled binaries carry the version.
#
# Writes <repo>\dist\build-info.json describing the build for the downstream packagers.

[CmdletBinding()]
param(
    [string]$Version,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$scriptDir = $PSScriptRoot
$repoRoot  = (Resolve-Path (Join-Path $scriptDir "..\..")).Path
$apps      = Join-Path $repoRoot "Apps"
$kernelDir = Join-Path $repoRoot "Kernels\build"

. (Join-Path $scriptDir "Get-BuildVersion.ps1")
$info = Get-BuildVersion -Version $Version
Write-Host "Version: $($info.Version)  release=$($info.IsRelease)  source=$($info.Source)  nameToken=$($info.NameToken)"

function Find-MSBuild {
    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $p = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
        if ($p -and (Test-Path $p)) { return $p }
    }
    $known = "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    if (Test-Path $known) { return $known }
    $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "MSBuild not found (install VS or add msbuild to PATH)."
}
$msb = Find-MSBuild
Write-Host "MSBuild: $msb"

$projects = [ordered]@{
    PcmHammer   = "UI\WindowsForms\PcmHammer\PcmHammer.csproj"
    PcmLogger   = "UI\WindowsForms\PcmLogger\PcmLogger.csproj"
    VpwExplorer = "UI\WindowsForms\VpwExplorer\VpwExplorer.csproj"
    Cli         = "UI\PcmHammerCLI\PcmHammerCLI.csproj"
}
# The same set Release.ps1 stamps (minus Tests, which isn't shipped, and the Uno app,
# which isn't built here) so a local release-style build versions every shipped binary
# - apps, the CLI, and the libraries - exactly like the CI release does.
$assemblyInfos = @(
    "UI\WindowsForms\PcmHammer\Properties\AssemblyInfo.cs",
    "UI\WindowsForms\PcmLogger\Properties\AssemblyInfo.cs",
    "UI\WindowsForms\VpwExplorer\Properties\AssemblyInfo.cs",
    "UI\PcmHammerCLI\Properties\AssemblyInfo.cs",
    "PcmLibraryWindowsApi\Properties\AssemblyInfo.cs",
    "UI\WindowsForms\PcmLibraryWindowsForms\Properties\AssemblyInfo.cs"
) | ForEach-Object { Join-Path $apps $_ }

# SDK-style projects carry the version in <Version> instead of AssemblyInfo.cs.
$versionedProjects = @(
    "PcmLibrary\PcmLibrary.csproj"
) | ForEach-Object { Join-Path $apps $_ }

# Stamps file-version properties (4-part) and the user-visible InformationalVersion
# (the display string, e.g. 2.0.0); appends InformationalVersion if the file lacks it.
function Set-AssemblyVersion {
    param([string]$File, [string]$FileVer, [string]$DisplayVer)
    $lines = Get-Content -LiteralPath $File
    $sawInfo = $false
    $out = foreach ($line in $lines) {
        if     ($line -match '^\[assembly:\s*AssemblyVersion\(')              { "[assembly: AssemblyVersion(""$FileVer"")]" }
        elseif ($line -match '^\[assembly:\s*AssemblyFileVersion\(')          { "[assembly: AssemblyFileVersion(""$FileVer"")]" }
        elseif ($line -match '^\[assembly:\s*AssemblyInformationalVersion\(') { $sawInfo = $true; "[assembly: AssemblyInformationalVersion(""$DisplayVer"")]" }
        else { $line }
    }
    if (-not $sawInfo) { $out = @($out) + "[assembly: AssemblyInformationalVersion(""$DisplayVer"")]" }
    Set-Content -LiteralPath $File -Value $out -Encoding UTF8
}

function Set-ProjectVersion {
    param([string]$File, [string]$Ver)
    $c = Get-Content -LiteralPath $File -Raw
    $c = $c -replace '<Version>[^<]*</Version>', "<Version>$Ver</Version>"
    Set-Content -LiteralPath $File -Value $c -NoNewline -Encoding UTF8
}

# Stop running instances so build output isn't locked.
Get-Process PcmHammer,pcmhammer-cli,PcmLogger,VpwExplorer -ErrorAction SilentlyContinue | Stop-Process -Force

# Back up the versioned files as raw bytes so we can restore them exactly afterwards.
$versionedFiles = $assemblyInfos + $versionedProjects
$backups = @{}
foreach ($f in $versionedFiles) { $backups[$f] = [System.IO.File]::ReadAllBytes($f) }

$staging = Join-Path $repoRoot "dist\staging"

try {
    if ($info.IsRelease) {
        foreach ($f in $assemblyInfos)     { Set-AssemblyVersion -File $f -FileVer $info.Version -DisplayVer $info.Display }
        foreach ($f in $versionedProjects) { Set-ProjectVersion  -File $f -Ver $info.Version }
        Write-Host "Stamped version $($info.Version) (display $($info.Display)) into AssemblyInfo + csproj (temporary)."
    }

    if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
    New-Item -ItemType Directory -Force -Path $staging | Out-Null

    foreach ($name in $projects.Keys) {
        $proj = Join-Path $apps $projects[$name]
        Write-Host "== Restoring $name =="
        & $msb $proj /t:Restore /v:minimal /nologo
        if ($LASTEXITCODE -ne 0) { throw "Restore failed: $name" }
        Write-Host "== Building $name ($Configuration) =="
        & $msb $proj /p:Configuration=$Configuration "/p:BuildTicks=$($info.Ticks)" /v:minimal /nologo /clp:ErrorsOnly
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $name" }
    }

    function Stage-App {
        param([string]$ProjRel, [string]$ExeName, [string]$DestSub)
        $bin = Join-Path (Split-Path (Join-Path $apps $ProjRel)) "bin\$Configuration"
        $exe = Get-ChildItem -Path $bin -Recurse -File -Filter $ExeName -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $exe) { throw "Built $ExeName not found under $bin" }
        $dest = Join-Path $staging $DestSub
        New-Item -ItemType Directory -Force -Path $dest | Out-Null
        Copy-Item -Path (Join-Path $exe.Directory.FullName "*") -Destination $dest -Recurse -Force
        Write-Host "Staged $ExeName -> $DestSub"
    }

    Stage-App -ProjRel $projects.PcmHammer   -ExeName "PcmHammer.exe"   -DestSub "WinForms\PcmHammer"
    Stage-App -ProjRel $projects.PcmLogger   -ExeName "PcmLogger.exe"   -DestSub "WinForms\PcmLogger"
    Stage-App -ProjRel $projects.VpwExplorer -ExeName "VpwExplorer.exe" -DestSub "WinForms\VpwExplorer"
    Stage-App -ProjRel $projects.Cli         -ExeName "pcmhammer-cli.exe" -DestSub "CLI"

    # Kernels next to PcmHammer (the GUI loads them from its exe folder).
    if (-not (Test-Path $kernelDir)) { throw "Kernel build dir not found: $kernelDir" }
    Copy-Item -Path (Join-Path $kernelDir "*.bin") -Destination (Join-Path $staging "WinForms\PcmHammer") -Force

    # Logger profiles / parameter files (read-only resources).
    $loggerSrc = Join-Path $apps "UI\WindowsForms\PcmLogger"
    $loggerDst = Join-Path $staging "WinForms\PcmLogger"
    Get-ChildItem -Path $loggerSrc -Filter "*.LogProfile" -ErrorAction SilentlyContinue | ForEach-Object { Copy-Item $_.FullName $loggerDst -Force }
    Get-ChildItem -Path $loggerSrc -Filter "Parameters.*.xml" -ErrorAction SilentlyContinue | ForEach-Object { Copy-Item $_.FullName $loggerDst -Force }

    $distDir = Join-Path $repoRoot "dist"
    New-Item -ItemType Directory -Force -Path $distDir | Out-Null
    [pscustomobject]@{
        Version       = $info.Version
        Display       = $info.Display
        NameToken     = $info.NameToken
        IsRelease     = $info.IsRelease
        Stamp         = $info.Stamp
        Configuration = $Configuration
        StagingRoot   = $staging
    } | ConvertTo-Json | Set-Content -Path (Join-Path $distDir "build-info.json") -Encoding UTF8

    Write-Host "Apps built and staged at $staging"
}
finally {
    foreach ($f in $versionedFiles) { [System.IO.File]::WriteAllBytes($f, $backups[$f]) }
    Write-Host "Restored versioned files (AssemblyInfo + csproj)."
}
