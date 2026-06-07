# PCM Hammer installer

Inno Setup script that packages the Windows apps into a single setup executable named
**`PCMHammer_<version>_Setup.exe`** (e.g. `PCMHammer_1.0.1.0_Setup.exe` for a release,
or `PCMHammer_<datestamp>_Setup.exe` for a development build).

## What it installs

Default location: **`C:\Program Files\PcmHammer`** (`{autopf}\PcmHammer`, admin).

Each app is a **separately selectable component**:

| Component | Contents | Folder | Default |
|-----------|----------|--------|---------|
| `pcmhammer`   | PCM Hammer + kernels | `{app}\PcmHammer` | on |
| `pcmlogger`   | PCM Logger + profiles | `{app}\PcmLogger` | on |
| `vpwexplorer` | VPW Explorer | `{app}\VpwExplorer` | **off** |
| `cli`         | `pcmhammer-cli.exe` + kernels | `{app}\Cli` | on |

Setup types: **Standard** (pcmhammer + pcmlogger + cli - the default), **Full** (adds
VPW Explorer), **Command-line tool only**, and **Custom**.

Start Menu group **"PCM Hammer"** holds a shortcut for each installed app plus Uninstall;
optional desktop shortcut for PCM Hammer. Fully uninstallable (Add/Remove Programs).

The installer/setup icon, the wizard images (`make-wizard-images.ps1`), and the shortcut
icon all come from the standard app icon
(`Apps/UI/WindowsForms/PcmHammer/0411_256px.ico`).

## Build locally

The normal path is `Apps\build\Build-All.ps1`, which builds the apps, stages them, and
calls this installer (and the portable). To build just the installer from an existing
staged layout:

```powershell
# After Apps\build\Build-Apps.ps1 has produced <repo>\dist\staging :
powershell -File Apps\installer\build-installer.ps1 `
    -StagingRoot <repo>\dist\staging -Version 1.0.1.0 `
    -SetupName PCMHammer_1.0.1.0_Setup -OutputDir <repo>\dist
#   → <repo>\dist\PCMHammer_1.0.1.0_Setup.exe
```

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) (its installer needs
elevation, so install it interactively once). Regenerate the wizard images if the app
icon changes: `powershell -File Apps\installer\make-wizard-images.ps1`.

## Silent / CLI-only install (e.g. from CI)

The setup is a standard Inno Setup installer, so it supports silent switches and
component selection. To install **only the command-line tool** and then use it:

```powershell
# Install just the CLI, no UI, to a chosen folder:
.\PCMHammer_1.0.1.0_Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS="cli" /DIR="C:\pcmhammer"

# Run it (kernels are installed alongside it):
& "C:\pcmhammer\Cli\pcmhammer-cli.exe" --get-properties --device COM6 --kernel-dir "C:\pcmhammer\Cli"
```

Useful switches: `/VERYSILENT`, `/SUPPRESSMSGBOXES`, `/NORESTART`, `/DIR=`, `/LOG=...`,
and `/COMPONENTS="..."` with any of `pcmhammer`, `pcmlogger`, `vpwexplorer`, `cli`
(e.g. `/COMPONENTS="pcmhammer,cli"`). Uninstall silently with
`"{app}\unins000.exe" /VERYSILENT`.

## CI

The `Installer` job in `.github/workflows/CheckBuild.yml` builds the setup after the
apps build, and uploads it as a downloadable artifact. See that file for details.
