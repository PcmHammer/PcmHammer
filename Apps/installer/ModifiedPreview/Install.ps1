param(
    [string]$DestinationDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\PCM Hammer Modified Preview'),
    [switch]$NoShortcuts,
    [switch]$ShowWizard
)
$ErrorActionPreference = 'Stop'
function Install-PcmHammer([bool]$DesktopShortcut, [bool]$StartMenuShortcut) {
$payload = Join-Path $PSScriptRoot 'App'
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'payload-sha256.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest) {
    $sourceFile = Join-Path $payload $entry.Name
    if ((Get-FileHash -LiteralPath $sourceFile -Algorithm SHA256).Hash -ne $entry.Sha256) {
        throw ('Package verification failed: ' + $entry.Name)
    }
}
$destination = [IO.Path]::GetFullPath($DestinationDirectory)
if ($destination -eq [IO.Path]::GetFullPath($payload)) { throw 'Choose a different installation directory.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($entry in $manifest) {
    $targetFile = Join-Path $destination $entry.Name
    Copy-Item -LiteralPath (Join-Path $payload $entry.Name) -Destination $targetFile -Force
    if ((Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash -ne $entry.Sha256) {
        throw ('Installed file verification failed: ' + $entry.Name)
    }
}
if ($DesktopShortcut -or $StartMenuShortcut) {
    $folders = @()
    if ($DesktopShortcut) { $folders += [Environment]::GetFolderPath('DesktopDirectory') }
    if ($StartMenuShortcut) { $folders += [Environment]::GetFolderPath('Programs') }
    $shell = New-Object -ComObject WScript.Shell
    foreach ($folder in $folders) {
    $shortcut = $shell.CreateShortcut((Join-Path $folder 'PCM Hammer Modified Preview.lnk'))
    $shortcut.TargetPath = Join-Path $destination 'PCMHammer.exe'
    $shortcut.WorkingDirectory = $destination
    $shortcut.Description = 'PCM Hammer personal preview - bench testing required'
    $shortcut.IconLocation = (Join-Path $destination 'PCMHammer.exe') + ',0'
    $shortcut.Save()
    }
}
return $destination
}
if (-not $ShowWizard) {
    $installed = Install-PcmHammer (-not $NoShortcuts) (-not $NoShortcuts)
    Write-Host ('PCM Hammer installed successfully to: ' + $installed)
    return
}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$form = New-Object Windows.Forms.Form
$form.Text = 'PCM Hammer Setup'
$form.ClientSize = New-Object Drawing.Size(540,350)
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object Drawing.Font('Segoe UI',10)
$form.BackColor = [Drawing.Color]::FromArgb(240,245,249)
try { $form.Icon = [Drawing.Icon]::ExtractAssociatedIcon((Join-Path $PSScriptRoot 'App\PCMHammer.exe')) } catch { }
$title = New-Object Windows.Forms.Label
$title.Text = 'Install PCM Hammer'
$title.Font = New-Object Drawing.Font('Segoe UI',19,[Drawing.FontStyle]::Bold)
$title.SetBounds(24,20,490,40)
$form.Controls.Add($title)
$description = New-Object Windows.Forms.Label
$description.Text = "Unofficial modified preview 2.0.0.12`nClose PCM Hammer before installing this update."
$description.SetBounds(26,66,485,48)
$form.Controls.Add($description)
$desktop = New-Object Windows.Forms.CheckBox
$desktop.Text = 'Create a desktop shortcut'
$desktop.Checked = $true
$desktop.SetBounds(26,125,480,26)
$form.Controls.Add($desktop)
$startMenu = New-Object Windows.Forms.CheckBox
$startMenu.Text = 'Create a Start menu shortcut'
$startMenu.Checked = $true
$startMenu.SetBounds(26,155,480,26)
$form.Controls.Add($startMenu)
$launch = New-Object Windows.Forms.CheckBox
$launch.Text = 'Open PCM Hammer after installation'
$launch.Checked = $true
$launch.SetBounds(26,185,480,26)
$form.Controls.Add($launch)
$status = New-Object Windows.Forms.Label
$status.Text = 'Installs for your Windows account. No administrator access needed.'
$status.SetBounds(26,225,485,50)
$form.Controls.Add($status)
$install = New-Object Windows.Forms.Button
$install.Text = 'Install'
$install.SetBounds(282,290,108,36)
$form.Controls.Add($install)
$cancel = New-Object Windows.Forms.Button
$cancel.Text = 'Cancel'
$cancel.SetBounds(402,290,108,36)
$cancel.Add_Click({ $form.Close() })
$form.Controls.Add($cancel)
$form.AcceptButton = $install
$form.CancelButton = $cancel
$install.Add_Click({
    $install.Enabled = $false
    $cancel.Enabled = $false
    $status.Text = 'Verifying and installing the app...'
    $form.Refresh()
    try {
        $installed = Install-PcmHammer $desktop.Checked $startMenu.Checked
        $status.Text = 'Installation complete.'
        [void][Windows.Forms.MessageBox]::Show($form,"PCM Hammer installed successfully.`n`nLocation: $installed`n`nYour selected shortcuts are ready.",'PCM Hammer Setup','OK','Information')
        if ($launch.Checked) {
            Start-Process -FilePath (Join-Path $installed 'PCMHammer.exe') -WorkingDirectory $installed
        }
        $form.Close()
    } catch {
        $status.Text = 'Installation did not finish.'
        [void][Windows.Forms.MessageBox]::Show($form,($_.Exception.Message + "`n`nClose the app and extract the entire ZIP before trying again."),'PCM Hammer Setup','OK','Error')
        $install.Enabled = $true
        $cancel.Enabled = $true
    }
})
[void]$form.ShowDialog()
