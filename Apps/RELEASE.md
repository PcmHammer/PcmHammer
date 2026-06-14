# PcmHammer Build & Release Process

How PcmHammer is built and packaged - for both **standard (development) builds** and
**official releases** - covering the produced targets, their filenames, versioning,
the shared build scripts, how to build locally and in CI, code signing, and a
release checklist.

> **Single source of truth:** the real build/packaging logic lives in PowerShell
> scripts under `Apps/build/` (and `Apps/installer/`). GitHub Actions is a thin
> wrapper that calls those same scripts, so a build done at home and a build done in
> CI produce identical artifacts - there is no separate, drift-prone process to keep
> in sync.

---

## 1. Standard vs Release builds

| | **Standard (development)** | **Release** |
|---|---|---|
| Trigger | Any commit/PR with **no** git tag | Commit with an **`x.x.x.x` git tag** (created by the release scripting) |
| Version source | Shared **date stamp** `YYYYMMDD_HHMMSS` (UTC) | The **tag** `x.x.x.x` |
| In-app line | `Build: <date time>` | `Version: x.x.x.x` |
| Configuration | Debug in CI (check build); Release locally | **Release** |
| Uno targets | **Built** (experimental) | **Built** (experimental) but **not attached** to the GitHub Release |
| Published to | Workflow artifacts | **GitHub Release page** (installer + portable) + workflow artifacts |

The date stamp is computed **once per build** and used for every filename *and*
embedded into the executables, so the in-app build date matches the download name
(see §3).

---

## 2. Targets and filenames

Example stamp `20260606_143052`, example release version `1.2.3.4`.

| Target | Standard (dev) filename | Release filename | Built on release? |
|--------|-------------------------|------------------|-------------------|
| **WinForms installer** | `PCMHammer_20260606_143052_Setup.exe` | `PCMHammer_1.2.3.4_Setup.exe` | ✅ |
| **WinForms portable** | `PCMHammer_20260606_143052_Portable.zip` | `PCMHammer_1.2.3.4_Portable.zip` | ✅ |
| **Uno Windows** (experimental) | `PCMHammer_Uno_Windows_experimental_…` | built, **workflow artifact only** (not on Release page) | ⚠️ experimental |
| **Uno Android APK** (experimental) | `PCMHammer_Uno_Android_experimental_…` | built, **workflow artifact only** (not on Release page) | ⚠️ experimental |

### Contents

**WinForms installer** - `Program Files\PcmHammer`, admin. Start Menu group
**"PCM Hammer"**: PCM Hammer, PCM Logger, VPW Explorer, Uninstall. Optional desktop
shortcut.

```
{app}\PcmHammer\     PcmHammer.exe   + dependency DLLs + Kernel-*.bin / Loader-*.bin
{app}\PcmLogger\     PcmLogger.exe   + DLLs + *.LogProfile + Parameters.*.xml
{app}\VpwExplorer\   VpwExplorer.exe + DLLs
{app}\Cli\           pcmhammer-cli.exe + Kernel-*.bin / Loader-*.bin
```

**WinForms portable** - a zip with one top-level `PCMHammer\` folder holding the same
programs (per-app subfolders) and kernels, **no installer**:
```
PCMHammer_<ver>_Portable.zip
└── PCMHammer\
    ├── PcmHammer\     PcmHammer.exe + DLLs + kernels
    ├── PcmLogger\     PcmLogger.exe + DLLs + profiles
    ├── VpwExplorer\   VpwExplorer.exe + DLLs
    └── Cli\           pcmhammer-cli.exe + kernels
```

**Uno Windows installer** (experimental) - self-contained single exe + **external**
kernels. Installs to `Program Files\PCMHammer_UNO`; a separate AppId so it coexists
with the WinForms install, but its shortcut **"PCM Hammer UNO"** goes in the **shared
"PCM Hammer"** Start Menu group.

**Uno Android APK** (experimental) - single `.apk`.

---

## 3. Versioning

`AppInfo.GetVersionOrBuildLine` (`Apps/PcmLibraryWindowsApi/AppInfo.cs`) decides what
the app prints:

- `AssemblyFileVersion` is `0.0.0.0`/empty → `Build: <date time>` (development).
- `AssemblyFileVersion` is set → `Version: <AssemblyInformationalVersion>` (release).

**Two version forms, by design** (driven by `Release.ps1`):
- **File properties** (`AssemblyVersion` / `AssemblyFileVersion`, and the SDK-style
  `<Version>`) follow the Microsoft `w.x.y.z` convention and are always **4-part**
  (a 3-part tag like `2.0.0` is padded to `2.0.0.0`). This is what right-click >
  Properties shows on the exe/dll.
- **User-visible string** (`AssemblyInformationalVersion`, shown in-app, and the Uno
  `ApplicationDisplayVersion`, and `help.html`) matches the **git tag verbatim** -
  so a `2.0.0` tag shows `Version: 2.0.0`, while a `2.0.0.1` tag shows `2.0.0.1`.

So for a release `Release.ps1 2.0.0` produces, e.g.:
  ```csharp
  [assembly: AssemblyVersion("2.0.0.0")]               // file properties (w.x.y.z)
  [assembly: AssemblyFileVersion("2.0.0.0")]           // file properties (w.x.y.z)
  [assembly: AssemblyInformationalVersion("2.0.0")]    // user-visible (matches tag)
  ```
  `Release.ps1` updates every shipped assembly: the four entry apps
  (`PcmHammer`, `PcmLogger`, `VpwExplorer`, and the **CLI** at
  `Apps/UI/PcmHammerCLI/Properties/AssemblyInfo.cs`), the libraries
  (`PcmLibraryWindowsApi`, `PcmLibraryWindowsForms`, `PcmLibrary.csproj <Version>`),
  the Uno app, and `help.html`.
- **Development**: `AssemblyFileVersion` stays `0.0.0.0`; the app shows `Build: <date>`.

### Consistent build date across all files

The build computes one timestamp and uses it everywhere:
1. In every artifact **filename** (`PCMHammer_<stamp>…`).
2. Embedded into the two entry exes via the `BuildTicks` MSBuild property (the `Date`
   target in `PcmHammer.csproj` / `PcmHammerCLI.csproj` uses `BuildTicks` if set,
   else `UtcNow`). So the in-app `Build:` line matches the download name.

---

## 4. Kernels and user data

- **Kernels are external everywhere.** The apps load `Kernel-*.bin` / `Loader-*.bin`
  from disk via `Vehicle.LoadKernelFromFile` (`Apps/PcmLibrary/Vehicle.Kernel.cs`):
  - **WinForms** and **Uno Windows** read them from the app folder. (Uno uses
    `AppContext.BaseDirectory` so this works even for single-file publishes.)
  - **CLI** reads them from `--kernel-dir <path>`, or the current working directory
    when omitted (`Program.ResolveKernelDir`).
- **User settings live in `%LOCALAPPDATA%`, not the install dir.** All PcmHammer
  settings are .NET *User-scoped* (`Properties.Settings`), so a `Program Files`
  (privileged) install needs no special handling - settings, logs and saved reads go
  to the user profile / user-chosen paths, never the read-only install folder.
- The single exe does **not** bundle vendor J2534 DLLs - those come from the
  interface drivers (OBDX Pro, Mongoose, MDI, …).

---

## 5. The build scripts (single source of truth)

`Apps/build/` (PowerShell, each runnable standalone):

| Script | Purpose |
|--------|---------|
| `Get-BuildVersion.ps1` | Decide the version: exact `x.x.x.x` tag on `HEAD` → release `x.x.x.x`; else dev stamp `YYYYMMDD_HHMMSS`. Overridable with `-Version`. Also returns the `BuildTicks`. |
| `Build-Apps.ps1` | Restore + build **Release** of PcmHammer, PcmLogger, VpwExplorer, CLI (passes `BuildTicks`). |
| `Build-Portable.ps1` | Assemble + zip the portable package. |
| `Build-UnoWindows.ps1` / `Build-Android.ps1` | Uno self-contained Windows exe + installer / Android APK (dev-only). |
| `Build-All.ps1` | Orchestrator: `-Version`, `-IncludeUno`; calls the above and the installer. |

`Apps/installer/`:
- `pcmhammer-setup.iss` - WinForms installer (Inno Setup). Output name set via
  `/DSetupName=…`.
- `uno-setup.iss` - Uno Windows installer.
- `build-installer.ps1` - stages app output + kernels and runs ISCC locally.
- `README.md` - installer details, incl. silent / CLI-only install.

CI (`.github/workflows/CheckBuild.yml`) only does the CI-specific parts -
install toolchains, call the scripts, upload artifacts - so there is no duplicated
build logic.

---

## 6. How to build

### 6a. Standard build, locally
```powershell
pwsh Apps\build\Build-All.ps1
#   → PCMHammer_<stamp>.exe, PCMHammer_<stamp>_Portable.zip
#   (+ Uno targets if -IncludeUno)
```

### 6b. Release-style build, locally (for home testing before tagging)
```powershell
# Force a release version by hand:
pwsh Apps\build\Build-All.ps1 -Version 1.2.3.4
#   → PCMHammer_1.2.3.4.exe, PCMHammer_1.2.3.4_Portable.zip

# …or check out the tagged commit and let it auto-detect:
git checkout 1.2.3.4
pwsh Apps\build\Build-All.ps1
```
These produce the **same** files CI does, because CI runs the same scripts.

### 6c. CI (GitHub Actions)
`CheckBuild.yml` runs on every push/PR and on tags:
1. **Kernels** (Ubuntu) - build the m68k kernels, publish as a temp artifact.
2. **Applications** (Windows) - download kernels, decide config + version (tag →
   **Release** named after the tag; otherwise **Debug** with a date stamp), build,
   and call the packaging scripts.
3. **Installer/Packaging** - produce `PCMHammer_<token>_Setup.exe`, the portable zip,
   and the Uno installer + APK; upload each as a workflow artifact. `<token>` is the
   git tag on a release (e.g. `2.0.0`) or `YYYYMMDD_HHMMSS` otherwise.
4. **GitHub Release** - on a tag push (`x.x.x` or `x.x.x.x`) the workflow creates a
   GitHub Release named after the tag and attaches the **WinForms installer +
   portable zip**. The Uno targets are still built but remain workflow artifacts only
   (experimental, not attached to the Release).

> Running the workflow YAML itself locally (e.g. `act`) is **not** supported - it is
> Linux/Docker-based and can't run the Windows + WinUI/Uno toolchain. Use the scripts
> in §6a/§6b instead.

---

## 7. Branching & tagging

- **Tags** are `x.x.x` or `x.x.x.x` (both valid), created by `Release.ps1` on the
  release commit. The user-visible version matches the tag exactly; the binaries'
  file-version properties are always padded to 4-part. (The older date-style tags in
  history - e.g. `2025.02.04` - are legacy.)
- **Release branches** are named `Release/<version>` using the dotted version, e.g.
  `Release/2.0.0`. (Legacy 1.x releases used an opaque counter - `Release/001` …
  `Release/021` - which is retained in history but **not** continued; the 2.x line
  starts the version-named convention. Old numeric tags also remain as-is - they are
  real versions in the wild.) A release branch exists so a hotfix can be applied and
  re-tagged independently of `develop`, even though in practice we usually cut a new
  release rather than hotfix an old one.

```powershell
# from the git root (the PcmHammer/ directory)
git checkout develop; git pull
git checkout -b Release/2.0.0
# Release.ps1 stamps every assembly + the Uno app + help.html, commits "Release x.x.x",
# and creates the git tag. Pass the user-visible version; file versions are padded to 4-part.
pwsh .\Release.ps1 2.0.0
# push the branch and the tag - the tag push is what triggers the release build + GitHub Release
git push origin Release/2.0.0 2.0.0
```
> Do **not** commit/push on a maintainer's behalf without explicit approval - these
> commands are the recipe; a human runs them.

---

## 8. Code signing

Sign the final executables **and** both installers, after building. Timestamp so the
signatures outlive the certificate.

```powershell
$signtool = "C:\Program Files (x86)\Windows Kits\10\bin\<sdk-ver>\x64\signtool.exe"
$ts = "http://timestamp.digicert.com"   # any RFC3161 timestamp server
& $signtool sign /fd SHA256 /tr $ts /td SHA256 /a "<file>.exe"
# verify: signtool verify /pa /v <file>.exe
```
Inno Setup can invoke signtool automatically via a `SignTool` directive so the
installer is signed as part of the compile.

Certificate options: **OV** (cheapest, SmartScreen reputation builds over time),
**EV** (immediate reputation, HSM/token), or **Azure Trusted Signing** (cloud, low
cost). Decision pending - see §12.

---

## 9. Prerequisites

Local build machine:
- **.NET SDK** (per `actions/setup-dotnet`, currently `10.0.x`) and the **.NET
  Framework 4.8 Developer Pack** (the WinForms/CLI apps are net48).
- **Inno Setup 6** - <https://jrsoftware.org/isinfo.php>.
- **Kernels**: either the prebuilt `Kernels\build\*.bin`, or the `m68k-linux-gnu`
  toolchain (via WSL) to build them.
- **Uno (only if building Uno targets)**: Uno workloads + (for Android) Java 17 and
  the Android SDK.
- **Code signing**: Windows SDK `signtool.exe` + a certificate (§8).

---

## 10. Release checklist

- [ ] `develop` builds clean and is what you intend to ship.
- [ ] Kernels current (`Kernels/build/*.bin`).
- [ ] `Release/<version>` branch created (e.g. `Release/2.0.0`); `Release.ps1` sets the
      versions, commits, and tags.
- [ ] Tag `x.x.x.x` created on the release commit.
- [ ] CI release build is green; `PCMHammer_x.x.x.x.exe` + `_Portable.zip` produced
      (Uno targets correctly **absent**).
- [ ] Smoke-test the installer and portable on a clean machine (device connects,
      kernel loads).
- [ ] Executables + installer code-signed and timestamped (`signtool verify` passes).
- [ ] GitHub Release created with notes; version-named artifacts attached.

---

## 11. Implementation status

What exists today vs. what is still to be wired up (this doc describes the agreed
end state):

**Done**
- `Apps/build/` scripts: `Get-BuildVersion.ps1` (tag→`x.x.x.x` else date stamp,
  `-Version` override), `Build-Apps.ps1` (Release build of all four apps + version
  stamping + staging), `Build-Portable.ps1`, `Build-All.ps1`.
- `BuildTicks` stamping in `PcmHammer.csproj` / `PcmHammerCLI.csproj` (shared build time).
- WinForms installer script `Apps/installer/pcmhammer-setup.iss` (PcmHammer, PcmLogger,
  VPW Explorer, CLI, kernels; "PCM Hammer" menu group; components incl. CLI-only;
  silent install) with parameterized output name; `build-installer.ps1` consumes the
  staged layout and emits `PCMHammer_<ver>.exe`.
- WinForms **portable** target (`PCMHammer_<ver>_Portable.zip`).
- CLI uses external kernels (`--kernel-dir`, default CWD).
- Verified locally: `Build-All.ps1 -Version 1.0.1.0` builds the apps (FileVersion
  1.0.1.0 baked in, AssemblyInfo restored clean) and the portable. Installer compile
  pending a local Inno Setup install (see note).
- `Release.ps1` stamps all shipped assemblies (incl. the CLI), libraries, the Uno app,
  and `help.html`; commits and tags. File versions are 4-part `w.x.y.z`; the
  user-visible string matches the tag.
- CI (`CheckBuild.yml`): builds **Release** on tag pushes (Debug otherwise), names
  artifacts after the tag, and **auto-creates a GitHub Release** with the WinForms
  installer + portable attached.

**Pending**
- **Uno Windows installer** (`uno-setup.iss`), self-contained single-exe publish, and
  the `AppContext.BaseDirectory` fix in `ConnectionService.cs` (needed so external
  kernels load under single-file).
- **Uno** is currently built on every run (including release tags) and attached only as
  experimental workflow artifacts. A formal gate to skip it on releases is **not**
  implemented (by decision - Uno will be promoted later).

> Local installer build needs **Inno Setup 6** installed (its installer requires
> elevation, so it must be run interactively once). After that,
> `Apps\build\Build-All.ps1` produces the installer too.

---

## 12. Open decisions

- **Signing certificate**: OV / EV / Azure Trusted Signing (cost vs SmartScreen).
- **Uno Windows "single exe" caveat**: WinUI/WindowsAppSDK can't always collapse to a
  literal single `.exe` (`PublishSingleFile` + self-contained, `WindowsPackageType=None`
  often yields the exe plus a few native DLLs). "Single self-contained app folder" may
  be the realistic outcome; the installer hides this.
- **Optional WinForms single-file** (Costura.Fody): only needed if you want the
  *portable* to be one `.exe` instead of an app folder. Requires migrating
  `PcmHammer.csproj` from `packages.config` to PackageReference. Not required for the
  installer.
