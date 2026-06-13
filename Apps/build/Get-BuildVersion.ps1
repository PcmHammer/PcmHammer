# Decides the build version and naming token.
#
# Rules:
#   -Version x.x.x / x.x.x.x   → forced release-style build (used for local release testing)
#   exact x.x.x / x.x.x.x tag on HEAD → release build, version from the tag
#   otherwise          → development build, naming token is a UTC date stamp
#
# Returns both forms (mirrors Release.ps1, so a local build matches the CI release):
#   Display = the version exactly as given/tagged (e.g. 2.0.0)  -> user-visible string
#             (AssemblyInformationalVersion / the app UI) and the artifact name token.
#   Version = padded 4-part w.x.y.z (e.g. 2.0.0.0)              -> file properties
#             (AssemblyVersion / AssemblyFileVersion / <Version>).
#
# Dot-source this file and call Get-BuildVersion, or run it directly to print the result.

function Get-BuildVersion {
    [CmdletBinding()]
    param([string]$Version)

    $now   = [DateTime]::UtcNow
    $stamp = $now.ToString("yyyyMMdd_HHmmss")
    $ticks = $now.Ticks
    $threePart = '^\d+\.\d+\.\d+$'
    $fourPart  = '^\d+\.\d+\.\d+\.\d+$'

    function New-ReleaseInfo([string]$Given, [string]$Source) {
        $fileVer = if ($Given -match $threePart) { "$Given.0" } else { $Given }   # pad to 4-part
        [pscustomobject]@{ IsRelease=$true; Display=$Given; Version=$fileVer; NameToken=$Given; Stamp=$stamp; Ticks=$ticks; Source=$Source }
    }

    if ($Version) {
        if ($Version -notmatch $threePart -and $Version -notmatch $fourPart) {
            throw "Version must be x.x.x or x.x.x.x, got '$Version'."
        }
        return New-ReleaseInfo $Version "forced"
    }

    $tag = (& git describe --exact-match --tags 2>$null)
    if ($LASTEXITCODE -eq 0 -and $tag) {
        $tag = $tag.Trim()
        if ($tag -match $threePart -or $tag -match $fourPart) {
            return New-ReleaseInfo $tag "tag"
        }
    }

    # Development build: no release version; the in-app line shows "Build: <date>".
    return [pscustomobject]@{ IsRelease=$false; Display="0.0.0.0"; Version="0.0.0.0"; NameToken=$stamp; Stamp=$stamp; Ticks=$ticks; Source="dev" }
}

if ($MyInvocation.InvocationName -ne '.') {
    Get-BuildVersion -Version $args[0]
}
