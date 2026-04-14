@setlocal
@echo off

goto beginning
* Create a non parsed area for header, notes and routines (call :label).
**********************************************************************
*
* Name         : BuildAll.cmd
* Description  : Build All of PcmHammer's kernels.
* Author       : Gampy <pcmhacking.net>
* Authored Date: 04/11/2022
* Revision Date: 03/01/2023 - Merged P04
* Revision Date: 03/25/2023 - Gampy <pcmhacking.net> Updated for new Assembly Kernels and Loaders.
* Revision Date: 05/23/2023 - Antus <pcmhacking.net> Update P04 loader address.
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

REM P04_Early uses the P04 loader
for %%A in (
  "-pP01 -aFF8000 -x",
  "-pP04 -aFF8000 -lFF9890 -x",
  "-pP04_Early -aFF8000 -x",
  "-pP05 -aFFC100 -X",
  "-pP08 -aFFAC00 -x",
  "-pP10 -aFFB800 -x",
  "-pP11 -aFFC000 -x",
  "-pP12 -aFF2000",
  "-pE54 -aFF9100 -x",
  "-pBlackBox -aFFC300 -x"
  ) do call "%BUILD_CMD%" %%~A %*

if not defined DISABLE_COPY call :CopyToDetectedTargets

popd
pause
goto :EOF

:CopyToDetectedTargets
set COPY_TARGET_COUNT=0

rem Windows Forms targets (stable locations)
call :CopyBinsToTarget "..\Apps\UI\WindowsForms\PcmHammer\bin\Debug"
call :CopyBinsToTarget "..\Apps\UI\WindowsForms\PcmHammer\bin\Release"

rem Uno targets (detected by output folder patterns)
call :CopyToDetectedUnoTargets

if "%COPY_TARGET_COUNT%" == "0" (
  echo No output targets detected. Kernels remain in %cd%.
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

if exist "Kernel-*.bin" (
  echo   Copying Kernel-*.bin to "%TARGET%"
  copy /Y Kernel-*.bin "%TARGET%\" 1>nul 2>nul
)

if exist "Loader-*.bin" (
  echo   Copying Loader-*.bin to "%TARGET%"
  copy /Y Loader-*.bin "%TARGET%\" 1>nul 2>nul
)

set /a COPY_TARGET_COUNT+=1
goto :EOF
