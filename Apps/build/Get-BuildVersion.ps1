# Decides the build version and naming token.
#
# Rules:
#   -Version x.x.x.x   → forced release-style build (used for local release testing)
#   exact x.x.x.x tag on HEAD → release build, version from the tag
#   otherwise          → development build, naming token is a UTC date stamp
#
# Dot-source this file and call Get-BuildVersion, or run it directly to print the result.

function Get-BuildVersion {
    [CmdletBinding()]
    param([string]$Version)

    $now   = [DateTime]::UtcNow
    $stamp = $now.ToString("yyyyMMdd_HHmmss")
    $ticks = $now.Ticks
    $fourPart = '^\d+\.\d+\.\d+\.\d+$'

    if ($Version) {
        if ($Version -notmatch $fourPart) { throw "Version must be x.x.x.x, got '$Version'." }
        return [pscustomobject]@{ IsRelease=$true; Version=$Version; NameToken=$Version; Stamp=$stamp; Ticks=$ticks; Source="forced" }
    }

    $tag = (& git describe --exact-match --tags 2>$null)
    if ($LASTEXITCODE -eq 0 -and $tag) {
        $tag = $tag.Trim()
        if ($tag -match $fourPart) {
            return [pscustomobject]@{ IsRelease=$true; Version=$tag; NameToken=$tag; Stamp=$stamp; Ticks=$ticks; Source="tag" }
        }
    }

    # Development build: no release version; the in-app line shows "Build: <date>".
    return [pscustomobject]@{ IsRelease=$false; Version="0.0.0.0"; NameToken=$stamp; Stamp=$stamp; Ticks=$ticks; Source="dev" }
}

if ($MyInvocation.InvocationName -ne '.') {
    Get-BuildVersion -Version $args[0]
}
