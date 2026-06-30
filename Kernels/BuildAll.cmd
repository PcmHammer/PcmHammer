@setlocal
@echo off

goto beginning
* Create a non parsed area for header, notes and routines (call :label).
**********************************************************************
*
* Name         : BuildAll.cmd
* Description  : Build All of PcmHammer's kernels.
* Author       : Gampy <pcmhacking.net>
* Authored Date: 2022-04-11
* Revision Date: 2023-03-01 - Merged P04
* Revision Date: 2023-03-25 - Gampy <pcmhacking.net> Updated for new Assembly Kernels and Loaders.
* Revision Date: 2023-05-23 - Antus <pcmhacking.net> Update P04 loader address.
* Revision Date: 2026-06-01 - Antus <pcmhacking.net> Restructure: 68k-VPW-C, 68k-VPW-Asm, 68k-VPW-Asm-P04; build/ for outputs.
* Revision Date: 2026-06-18 - Use BuildKernel.cmd scripts in kernel dirs, or binary only artifact
*
* Authors disclaimer
*   It is what it is, you can do with it as you please. (with respect)
*
*   Just don't blame me if it teaches your computer to smoke!
*
*   -Enjoy
*
*
* NOTES:
*
*
**********************************************************************
* Here we'll collect the routines (call :label / goto label).
*
**
**
*
*************************************** Beginning
* Let us get to it!
:beginning
set BUILD_CMD=%~dp0Build.cmd
pushd "%~dp0"

rem * Handle command line options
rem * Block invalid command line arguments -a, -l and -p, they cannot be used in this context.
rem * They would need to be changed below.
(
  setlocal enabledelayedexpansion
  for %%A in (%*) do (
    set VAR=%%A
    if /i "!VAR:~0,2!" == "-a" echo Invalid argument & goto :EOF
    if /i "!VAR:~0,2!" == "-l" echo Invalid argument & goto :EOF
    if /i "!VAR:~0,2!" == "-p" echo Invalid argument & goto :EOF
    if /i "!VAR!" == "-c"      set DISABLE_COPY=True
    if /i "!VAR!" == "/h"      call "%BUILD_CMD%" %*
    if /i "!VAR!" == "-h"      call "%BUILD_CMD%" %*
    if /i "!VAR!" == "--help"  call "%BUILD_CMD%" %*
  )
  setlocal disabledelayedexpansion
)

rem * Build kernels with a BuildKernel.cmd.
echo Scanning kernel directories for build scripts...
for /d %%D in (*) do (
  if exist "%%D\BuildKernel.cmd" (
    echo Building kernel^(s^) in %%D ...
    call "%%D\BuildKernel.cmd" %*
  )
)

rem * Fall back to a binary artifact when a kernel directory  has no BuildKernel.cmd
call :CopyKernelDirBins

if not defined DISABLE_COPY call :CopyToDetectedTargets

popd
goto :EOF

:CopyKernelDirBins
for /d %%D in (*) do (
  if /i not "%%~nxD" == "build" (
    if not exist "%%D\BuildKernel.cmd" (
      for %%F in ("%%D\Kernel-*.bin" "%%D\Loader-*.bin") do (
        if exist "%%F" (
          if not exist "build\%%~nxF" (
            echo Using committed %%~nxF from %%D ^(no source built it^)
            if not exist build mkdir build
            copy /Y "%%F" "build\%%~nxF" 1>nul
          )
        )
      )
    )
  )
)
goto :EOF

:CopyToDetectedTargets
set COPY_TARGET_COUNT=0

rem Windows Forms targets. The Debug path is created if missing so a fresh checkout
rem (not yet built locally) still receives the kernels. The Release path is left to the
rem release scripting, so it is only populated when it already exists.
call :CopyBinsToTarget "..\Apps\UI\WindowsForms\PcmHammer\bin\Debug" create
call :CopyBinsToTarget "..\Apps\UI\WindowsForms\PcmHammer\bin\Release"

rem CLI targets (kernels are embedded at CLI build time from build\ dir,
rem but we copy here so the bin dir can be inspected to verify the right kernels were built).
rem Same policy: create Debug if missing, leave Release to the release scripting.
call :CopyBinsToTarget "..\Apps\UI\PcmHammerCLI\bin\Release"
call :CopyBinsToTarget "..\Apps\UI\PcmHammerCLI\bin\Debug" create

rem Linux CLI target. The published Linux build EMBEDS the kernels in the executable (see the
rem EmbeddedResource item in PcmHammerLinux.csproj), so a shipped binary needs no loose .bin
rem files. Kernels are still copied next to the build output because a loose file on disk takes
rem precedence over the embedded copy, which is what lets you test a freshly built kernel without
rem rebuilding the binary. The SDK output is nested per TFM/RID; create the default Debug output
rem dir so a fresh checkout has a populated place to run from, then also drop kernels next to any
rem already-built binary (CopyToDetectedLinuxTargets).
call :CopyBinsToTarget "..\Apps\UI\PcmHammerLinux\bin\Debug\net10.0\linux-x64" create
call :CopyToDetectedLinuxTargets

rem Uno targets (detected by output folder patterns)
call :CopyToDetectedUnoTargets

rem WPF targets
call :CopyBinsToTarget "..\Apps\UI\WPF\PCMHammer\bin\Debug\net10.0-windows"
call :CopyBinsToTarget "..\Apps\UI\WPF\PCMHammer\bin\Release\net10.0-windows"

if "%COPY_TARGET_COUNT%" == "0" (
  echo No output targets detected. Kernels remain in build\.
)

goto :EOF

:CopyToDetectedLinuxTargets
set "LINUX_BIN_ROOT=..\Apps\UI\PcmHammerLinux\bin"
if not exist "%LINUX_BIN_ROOT%" (
  echo Linux CLI bin root not found: "%LINUX_BIN_ROOT%"
  goto :EOF
)

echo Scanning Linux CLI bin output targets for the app binary...
for /r "%LINUX_BIN_ROOT%" %%F in (pcmhammer-cli.dll) do (
  echo   Found Linux CLI output: "%%~dpF"
  call :CopyBinsToTarget "%%~dpF"
)

goto :EOF

:CopyToDetectedUnoTargets
set "UNO_BIN_ROOT=..\Apps\UI\UnoUI\PcmHacking.UnoUI\bin"
if not exist "%UNO_BIN_ROOT%" (
  echo Uno bin root not found: "%UNO_BIN_ROOT%"
  goto :EOF
)

echo Scanning Uno bin output targets for app executables...
for /r "%UNO_BIN_ROOT%" %%F in (pcm*.exe) do (
  echo   Found app executable: "%%~fF"
  call :CopyBinsToTarget "%%~dpF"
)
for /r "%UNO_BIN_ROOT%" %%F in (pcm*.dll) do (
  echo   Found app assembly: "%%~fF"
  call :CopyBinsToTarget "%%~dpF"
)

goto :EOF

:CopyBinsToTarget
set "TARGET=%~1"
set "CREATE=%~2"
if not exist "%TARGET%" (
  if /i "%CREATE%"=="create" (
    echo Creating target: "%TARGET%"
    mkdir "%TARGET%"
  ) else (
    rem Release paths are made by the release scripting, and the Uno paths are deep and
    rem numerous, so we do not create those - only copy when they already exist.
    echo Target not found, skipping: "%TARGET%"
    goto :EOF
  )
)

echo Detected target: "%TARGET%"

if exist "build\Kernel-*.bin" (
  echo   Copying build\Kernel-*.bin to "%TARGET%"
  copy /Y build\Kernel-*.bin "%TARGET%\" 1>nul 2>nul
)

if exist "build\Loader-*.bin" (
  echo   Copying build\Loader-*.bin to "%TARGET%"
  copy /Y build\Loader-*.bin "%TARGET%\" 1>nul 2>nul
)

set /a COPY_TARGET_COUNT+=1
goto :EOF
