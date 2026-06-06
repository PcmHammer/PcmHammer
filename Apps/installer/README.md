# PCM Hammer installer

Inno Setup script that packages the Windows apps into a single **`setup.exe`**.

## What it installs

Default location: **`C:\Program Files\PcmHammer`** (`{autopf}\PcmHammer`, admin).

| Component | Contents | Folder |
|-----------|----------|--------|
| `gui` | PCM Hammer + kernels, PCM Logger, VPW Explorer | `{app}\PcmHammer`, `{app}\PcmLogger`, `{app}\VpwExplorer` |
| `cli` | `pcmhammer-cli.exe` + kernels | `{app}\Cli` |

Start Menu group **"PCM Hammer"**: PCM Hammer, PCM Logger, VPW Explorer, Uninstall.
Optional desktop shortcut for PCM Hammer. Fully uninstallable (Add/Remove Programs).

The installer icon and shortcut icon are the standard app icon
(`Apps/UI/WindowsForms/PcmHammer/0411_256px.ico`).

## Build locally

1. Build the apps in **Release** and build the kernels (`Kernels\build\*.bin`).
2. Install [Inno Setup 6](https://jrsoftware.org/isinfo.php).
3. Run:
   ```powershell
   pwsh Apps\installer\build-installer.ps1 -Version 2026.06.06
   ```
   → `Apps\installer\output\setup.exe`

The helper stages each app's `bin\Release` output into `Apps\installer\staging\`
in the layout the `.iss` expects, then runs ISCC. To run ISCC directly against a
pre-staged layout:
```powershell
iscc /DAppVersion=2026.06.06 /DWinFormsRoot=<...>\WinForms /DCliRoot=<...>\CLI Apps\installer\pcmhammer-setup.iss
```

## Silent / CLI-only install (e.g. from CI)

The generated `setup.exe` is a standard Inno Setup installer, so it supports silent
switches. To install **only the command-line tool** and then use it:

```powershell
# Install just the CLI, no UI, to a chosen folder:
.\setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS="cli" /DIR="C:\pcmhammer"

# Run it (kernels are installed alongside it):
& "C:\pcmhammer\Cli\pcmhammer-cli.exe" --get-properties --device COM6 --kernel-dir "C:\pcmhammer\Cli"
```

Useful switches: `/VERYSILENT`, `/SUPPRESSMSGBOXES`, `/NORESTART`, `/DIR=`,
`/COMPONENTS="cli"` (or `"gui,cli"`), `/LOG="setup.log"`. Uninstall silently with
`"{app}\unins000.exe" /VERYSILENT`.

## CI

The `Installer` job in `.github/workflows/CheckBuild.yml` builds `setup.exe` after
the apps build, and uploads it as a downloadable artifact. See that file for details.
