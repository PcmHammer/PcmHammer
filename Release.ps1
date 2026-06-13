param
(
    [String] $Version
)

function Show-Usage {
    Write-Host @"

PCM Hammer - release script
===========================

Cuts a release: stamps the version into every shipped assembly (PcmHammer, PcmLogger,
VpwExplorer, the CLI, the libraries and the Uno app) and into help.html, commits the
change as "Release <version>", and creates a matching git tag.

USAGE
    .\Release.ps1 <version>

    <version>   x.x.x  or  x.x.x.x      e.g.  2.0.0   or   2.0.0.1

HOW THE VERSION IS USED
    * The number you pass becomes the git TAG and the user-visible version shown in
      the app (e.g. the title bar / log shows "Version: 2.0.0").
    * The exe/dll FILE PROPERTIES (right-click > Properties > Details) always use the
      4-part Microsoft w.x.y.z form. A 3-part version is padded: 2.0.0 -> 2.0.0.0.

BEFORE YOU RUN  (this script commits and tags the CURRENT branch)
    1. Start from an up-to-date develop:
           git checkout develop ; git pull
    2. Create the release branch (dotted version names for the 2.x line):
           git checkout -b Release/<version>        # e.g. Release/2.0.0

AFTER YOU RUN
    Push the branch AND the tag - pushing the TAG is what triggers the CI release
    build and the GitHub Release (installer + portable):
           git push origin Release/<version> <version>

FULL EXAMPLE
    git checkout develop ; git pull
    git checkout -b Release/2.0.0
    .\Release.ps1 2.0.0
    git push origin Release/2.0.0 2.0.0

"@
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    Show-Usage
    exit 0
}

# Accept x.x.x (pad to x.x.x.0) or x.x.x.x (use as-is).
# The git tag keeps the user-supplied string; the assembly version is always x.x.x.x.
$Tag = $Version
if ($Version -match '^\d+\.\d+\.\d+$')
{
    $Version = "$Version.0"
}
elseif ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$')
{
    Write-Host "Version must be in x.x.x or x.x.x.x format. Examples: 2.0.1  or  2.0.1.1"
    exit 1
}

Write-Host "Assembly version : $Version   (file properties, w.x.y.z)"
Write-Host "Display version  : $Tag        (shown in the UI / installer)"
Write-Host "Git tag          : $Tag"

# AssemblyVersion / AssemblyFileVersion follow the Microsoft w.x.y.z convention and
# always use the padded 4-part $Version (so right-click > Properties shows 2.0.0.0).
# AssemblyInformationalVersion is the user-visible string and matches the git tag
# verbatim ($Tag), so the UI shows exactly what was tagged (e.g. "2.0.0" or "2.0.0.1").
# ---------------------------------------------------------------------------
function Update-AssemblyInfo {
    param([string]$File)
    if (-not (Test-Path $File)) { Write-Warning "Not found: $File"; return }
    Write-Host "  $File"
    $c = Get-Content $File -Raw
    $c = $c -replace 'AssemblyVersion\("[^"]*"\)',            "AssemblyVersion(`"$Version`")"
    $c = $c -replace 'AssemblyFileVersion\("[^"]*"\)',        "AssemblyFileVersion(`"$Version`")"
    if ($c -match 'AssemblyInformationalVersion')
    {
        $c = $c -replace 'AssemblyInformationalVersion\("[^"]*"\)', "AssemblyInformationalVersion(`"$Tag`")"
    }
    else
    {
        $c = $c.TrimEnd() + "`r`n[assembly: AssemblyInformationalVersion(`"$Tag`")]`r`n"
    }
    Set-Content $File $c -NoNewline
    git add $File
}
# ---------------------------------------------------------------------------

Write-Host "Updating AssemblyInfo files..."
# Libraries
Update-AssemblyInfo "Apps\PcmLibraryWindowsApi\Properties\AssemblyInfo.cs"
Update-AssemblyInfo "Apps\UI\WindowsForms\PcmLibraryWindowsForms\Properties\AssemblyInfo.cs"
Update-AssemblyInfo "Apps\Tests\Properties\AssemblyInfo.cs"
# Applications
Update-AssemblyInfo "Apps\UI\WindowsForms\PcmHammer\Properties\AssemblyInfo.cs"
Update-AssemblyInfo "Apps\UI\PcmHammerCLI\Properties\AssemblyInfo.cs"
Update-AssemblyInfo "Apps\UI\WindowsForms\PcmLogger\Properties\AssemblyInfo.cs"
Update-AssemblyInfo "Apps\UI\WindowsForms\VpwExplorer\Properties\AssemblyInfo.cs"

# PcmLibrary uses SDK-style <Version> instead of AssemblyInfo.cs
Write-Host "Updating PcmLibrary version..."
$pcmLibProj = "Apps\PcmLibrary\PcmLibrary.csproj"
if (Test-Path $pcmLibProj)
{
    Write-Host "  $pcmLibProj"
    $c = Get-Content $pcmLibProj -Raw
    $c = $c -replace '<Version>[^<]*</Version>', "<Version>$Version</Version>"
    Set-Content $pcmLibProj $c -NoNewline
    git add $pcmLibProj
}

Write-Host "Updating Uno application version..."
$unoProj = "Apps\UI\UnoUI\PcmHacking.UnoUI\PcmHacking.UnoUI.csproj"
if (Test-Path $unoProj)
{
    $parts = $Version.Split('.')
    # ApplicationVersion must be an integer; derive one from the four components.
    $appVersionInt = [int]$parts[0] * 1000000 + [int]$parts[1] * 10000 + [int]$parts[2] * 100 + [int]$parts[3]
    Write-Host "  $unoProj  (ApplicationVersion=$appVersionInt)"
    $c = Get-Content $unoProj -Raw
    # <Version> is the assembly version (w.x.y.z); ApplicationDisplayVersion is the
    # user-visible string and matches the tag.
    $c = $c -replace '<Version>[^<]*</Version>',                                     "<Version>$Version</Version>"
    $c = $c -replace '<ApplicationDisplayVersion>[^<]*</ApplicationDisplayVersion>', "<ApplicationDisplayVersion>$Tag</ApplicationDisplayVersion>"
    $c = $c -replace '<ApplicationVersion>[^<]*</ApplicationVersion>',               "<ApplicationVersion>$appVersionInt</ApplicationVersion>"
    Set-Content $unoProj $c -NoNewline
    git add $unoProj
}

Write-Host "Updating help.html..."
$helpFile = "Apps\UI\WindowsForms\PcmHammer\help.html"
if (Test-Path $helpFile)
{
    $c = Get-Content $helpFile -Raw
    $c = $c -replace '<h1>PCM Hammer[^<]*</h1>', "<h1>PCM Hammer $Tag</h1>"
    Set-Content $helpFile $c -NoNewline
    git add $helpFile
}

git commit -m "Release $Version"
git tag $Tag

Write-Host ""
Write-Host "Tagged as $Tag. Push when ready:"
Write-Host "  git push && git push origin $Tag"
