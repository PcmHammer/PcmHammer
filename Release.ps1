param
(
    [Parameter(Mandatory=$true)] [String] $Version
)

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

Write-Host "Assembly version : $Version"
Write-Host "Git tag          : $Tag"

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
        $c = $c -replace 'AssemblyInformationalVersion\("[^"]*"\)', "AssemblyInformationalVersion(`"$Version`")"
    }
    else
    {
        $c = $c.TrimEnd() + "`r`n[assembly: AssemblyInformationalVersion(`"$Version`")]`r`n"
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
Update-AssemblyInfo "Apps\UI\WindowsForms\PcmHammerCLI\Properties\AssemblyInfo.cs"
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
    $c = $c -replace '<Version>[^<]*</Version>',                                     "<Version>$Version</Version>"
    $c = $c -replace '<ApplicationDisplayVersion>[^<]*</ApplicationDisplayVersion>', "<ApplicationDisplayVersion>$Version</ApplicationDisplayVersion>"
    $c = $c -replace '<ApplicationVersion>[^<]*</ApplicationVersion>',               "<ApplicationVersion>$appVersionInt</ApplicationVersion>"
    Set-Content $unoProj $c -NoNewline
    git add $unoProj
}

Write-Host "Updating help.html..."
$helpFile = "Apps\UI\WindowsForms\PcmHammer\help.html"
if (Test-Path $helpFile)
{
    $c = Get-Content $helpFile -Raw
    $c = $c -replace '<h1>PCM Hammer[^<]*</h1>', "<h1>PCM Hammer $Version</h1>"
    Set-Content $helpFile $c -NoNewline
    git add $helpFile
}

git commit -m "Release $Version"
git tag $Tag

Write-Host ""
Write-Host "Tagged as $Tag. Push when ready:"
Write-Host "  git push && git push origin $Tag"
