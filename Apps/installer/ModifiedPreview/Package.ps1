param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Version = '2.0.0-local.12'
)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..\..'))
$stage = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'PCM-Hammer-Modified-Preview'
if (Test-Path -LiteralPath $stage) { throw 'Choose a new output directory to avoid mixing builds.' }
$payload = Join-Path $stage 'App'
New-Item -ItemType Directory -Path $payload -Force | Out-Null
& dotnet publish (Join-Path $repo 'Apps\UI\WPF\PCMHammer\PCMHammer.csproj') -c Release -r win-x86 --self-contained true -p:Version=$Version -o $payload
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
foreach ($name in @('LICENSE','THIRD-PARTY-NOTICES')) {
    Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $payload
}
foreach ($name in @('Install.cmd','Install.ps1','README.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage
}
$manifest = @(Get-ChildItem -LiteralPath $payload -File | ForEach-Object {
    [pscustomobject]@{Name=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
})
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'payload-sha256.json') -Encoding UTF8
Write-Host ('Prepared personal setup files at: ' + $stage)
