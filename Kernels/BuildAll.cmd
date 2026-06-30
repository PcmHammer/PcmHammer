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

rem P04_Early uses the P04 loader; P04 uses 68k-VPW-Asm-P04, all others use 68k-VPW-Asm or 68k-VPW-C.
for %%A in (
  "-pP01 -aFF8000 -x",
  "-pP04 -aFF8000 -lFF9890 -x",
  "-pP04_Early -aFF8000 -x",
  "-pP05 -aFFC100 -x",
  "-pP08 -aFFABE0 -x",
  "-pP10 -aFFB800 -x",
  "-pP11 -aFFC000 -x",
  "-pP12 -aFF2000 -x",
  "-pE54 -aFF9100 -x",
  "-pBlackBox -aFFC300 -x"
  ) do call "%BUILD_CMD%" %%~A %*

if not defined DISABLE_COPY call :CopyToDetectedTargets

popd
goto :EOF

:CopyToDetectedTargets
set COPY_TARGET_COUNT=0

rem Windows Forms targets (stable locations)
call :CopyBinsToTarget "..\Apps\UI\WindowsForms\PcmHammer\bin\Debug"
call :CopyBinsToTarget "..\Apps\UI\WindowsForms\PcmHammer\bin\Release"

rem CLI targets (kernels are embedded at CLI build time from build\ dir,
rem but we copy here so the bin dir can be inspected to verify the right kernels were built)
call :CopyBinsToTarget "..\Apps\UI\PcmHammerCLI\bin\Release"
call :CopyBinsToTarget "..\Apps\UI\PcmHammerCLI\bin\Debug"

rem Uno targets (detected by output folder patterns)
call :CopyToDetectedUnoTargets

rem WPF targets
call :CopyBinsToTarget "..\Apps\UI\WPF\PCMHammer\bin\Debug\net10.0-windows"
call :CopyBinsToTarget "..\Apps\UI\WPF\PCMHammer\bin\Release\net10.0-windows"

if "%COPY_TARGET_COUNT%" == "0" (
  echo No output targets detected. Kernels remain in build\.
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
if not exist "%TARGET%" (
  echo Target not found: "%TARGET%"
  goto :EOF
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
