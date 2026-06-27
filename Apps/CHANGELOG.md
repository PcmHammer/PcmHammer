# PCM Hammer Changelog

## Version x.x.x.x (Next)

### New PCM support

A large expansion of supported GM VPW PCMs, with per-OSID identification and
service-numbers (where known):

- **P04 / P04_Early (all variants)**, including a dedicated P04 assembly kernel
  fork pinned to a known-good layout, and correct 512&nbsp;KB vs 256&nbsp;KB
  handling when the P04_Early type is selected manually.
- **P05, P05b (VPW)** read and write support, with file-format detection.
  (P05c is CAN only and remains not supported)
- **P08** supported
- **P11** support: flash kernel, file-format detection and checksums, and
  boot-sector write (using a verified, cross-flash-compatible boot sector).
- **P12 / P12b** support on the assembly kernel, with write enabled and clear
  warnings before any operation that writes the slave (throttle) CPU.
- **E54** (Duramax) support.
- **98/99 Vortec "Black Box"** support, with checksum and old-style DLC / OBDLink
  CRC-read fixes.
- **P59** split out from P01, plus COS handling and many additional OSID entries.

### New applications and platforms

- **Command-line interface (`pcmhammer-cli`):** a scriptable CLI for read,
  test-read, write, test-write, verify, and identify-pcm, with device and
  external-kernel selection.
- **Uno cross-platform UI (Windows and Android) - experimental:** a new UI built
  on the Uno Platform, including read/write pages, a settings page, background
  data/progress services, Android storage-permission handling and APK packaging,
  and a keep-awake equivalent. Note that UNO versions are not ready for prime-time
  and will not yet be shipped in official releases.
- **Bluetooth support** (Android) via a dedicated .NET 10 Bluetooth
  library, with paired-device discovery.

### Kernels

- New **m68k assembly kernel build system** (alongside the original C kernels),
  driven from CI, covering the expanded PCM list.
- Fixed a **DLC RX FIFO overflow during CRC calculation** and an **early-silicon
  PCM start-up DLC crash**.
- Kernel size optimisations; ISO 8601 / build-epoch versioning; build-system
  hardening (including pinning the P04 kernel to a working layout).

### Devices

- **OBDX Pro** documented as the recommended interface; **GM/Bosch MDI and MDI2**,
  **Scanmatik** (long-packet support, firmware 2.21.23+), and **AVT 852** covered.
- **Automatic serial device-type detection** (with baud-rate switching) and
  guards against J2534 initialisation crashes and null/empty driver entries.
- **Serial-port hardening** to avoid lock-ups when accessing broken Bluetooth
  serial ports, and clean interface shutdown on app exit.

### Security and unlock

- Reworked unlock logic with cleaner failure handling, and the ability to ride
  out the PCM's power-on security time-delay lockout.
- Key **brute forcer**, and guidance for users whose security data is
  corrupted (seed `0000` / `FFFF`). No need to use an external tool.
- Skip the unlock step when the PCM is already in recovery mode.

### Safety and data integrity

- Detect a **missing parameter block** in P04 / P05 images to avoid soft-bricks
  from incomplete images found in the wild.
- **Brick-risk confirmation** before writing a PCM type still marked as under
  development.
- **Boot-sector write protection** blocks on hardware-write-protected sections of P05
  an brick risk with P11 / P12 as applicable.
- Use the **flash chip size** for the read size, and improved file validation,
  including validation with no device connected. Allows an unsupported PCM bin to be
  read as a known type if the kernel is compatible and key is known.

### Application improvements (WinForms)

- Reworked read/write dialogs, can select PCM type if OSID not known.
- **Halt Running Kernel** moved to the Tools menu; the app is now blocked from
  closing while an operation is running.
- The **MD5 of each loaded kernel/loader** is printed to the log for diagnostics.
- **Test File Checksums** is available with no device selected.
- Fixes for a settings-screen freeze and for crashes on second J2534 init and on
  the file-save picker.

### Build, release, and packaging

- **GitHub Actions overhaul** building WinForms, Uno Windows, and Uno Android,
  plus the kernels, and publishing downloadable artifacts.
- **Windows installer** (Inno Setup) with selectable components, plus a
  **portable** package can be just run and includes the CLI release.
- **Release-process documentation** (`Apps/RELEASE.md`) and shared build scripts
  (`Apps/build/`) used by both local builds and CI.
- Upgraded to **.NET Framework 4.6.2**, standardised dates to ISO 8601, fixed
  hundreds of nullability/warning issues for current .NET, and updated vulnerable
  packages.

---

> Note: the Uno (Windows and Android) builds are **experimental** and are intended
> for testing, not production use.
