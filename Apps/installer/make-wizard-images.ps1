# Generates the Inno Setup wizard images from the PCM Hammer app icon, replacing the
# default "box + CD" graphics. Produces (next to this script):
#   WizardImage.png       big left banner (welcome/finish pages)
#   WizardSmallImage.png   small top-right image (inner pages)
#
# Re-run this if the app icon changes. Inno Setup 6.3+ accepts PNG for these images.

[CmdletBinding()]
param(
    [string]$IconPath
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$dir = $PSScriptRoot
if (-not $IconPath) { $IconPath = Join-Path $dir "..\UI\WindowsForms\PcmHammer\0411_256px.ico" }
$IconPath = (Resolve-Path $IconPath).Path

# Largest frame of the icon as a bitmap.
$icon = New-Object System.Drawing.Icon($IconPath, 256, 256)
$logo = $icon.ToBitmap()

function New-WizardImage {
    param([int]$Width, [int]$Height, [double]$Fill, [string]$OutFile)
    $bmp = New-Object System.Drawing.Bitmap($Width, $Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode      = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode  = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode    = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::White)

    $target = [int]([Math]::Min($Width, $Height) * $Fill)
    $x = [int](($Width  - $target) / 2)
    $y = [int](($Height - $target) / 2)
    $g.DrawImage($logo, $x, $y, $target, $target)
    $g.Dispose()
    $bmp.Save((Join-Path $dir $OutFile), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "Wrote $OutFile ($Width x $Height)"
}

# Sizes cover up to 250% DPI scaling (Inno downscales as needed).
New-WizardImage -Width 410 -Height 797 -Fill 0.60 -OutFile "WizardImage.png"
New-WizardImage -Width 138 -Height 138 -Fill 0.88 -OutFile "WizardSmallImage.png"

$logo.Dispose()
