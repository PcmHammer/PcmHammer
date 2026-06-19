@echo off
rem ***
rem *** BuildKernel.cmd - build all assembly kernels sourced from this directory.
rem ***
rem *** Discovered and run by ..\BuildAll.cmd, and runnable on its own. It owns the
rem *** PCM list / addresses for this directory and drives the shared ..\Build.cmd
rem *** once per PCM. Extra arguments (%*) are forwarded through to Build.cmd.
rem ***
rem *** The single Kernel.S here builds every listed PCM via per-PCM #ifdef blocks;
rem *** they differ only in -p<PCM> and the base -a<address>.
rem ***
pushd "%~dp0.."
set "RC=0"
for %%A in (
  "-pP01 -aFF8000 -x -cpu68332",
  "-pP04_Early -aFF8000 -x -cpu68332",
  "-pP05 -aFFC100 -x -cpu68332",
  "-pP08 -aFFABE0 -x -cpu68332",
  "-pP10 -aFFB800 -x -cpu68332",
  "-pP11 -aFFC000 -x -cpu68332",
  "-pP12 -aFF2000 -x -cpu68332",
  "-pE54 -aFF9100 -x -cpu68332",
  "-pBlackBox -aFFC300 -x -cpu68332"
  ) do (
    call ".\Build.cmd" %%~A %*
    if errorlevel 1 set "RC=1"
  )
popd
exit /b %RC%
