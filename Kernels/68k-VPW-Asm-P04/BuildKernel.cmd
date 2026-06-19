@echo off
rem ***
rem *** BuildKernel.cmd - build the P04 assembly kernel and its loader.
rem ***
rem *** Discovered and run by ..\BuildAll.cmd, and runnable on its own. It owns the
rem *** PCM / address / loader address for this directory and drives ..\Build.cmd.
rem *** Extra arguments (%*) are forwarded through to Build.cmd.
rem ***
pushd "%~dp0.."
call ".\Build.cmd" -pP04 -aFF8000 -lFF9890 -x -cpu68332 %*
set "RC=%errorlevel%"
popd
exit /b %RC%
