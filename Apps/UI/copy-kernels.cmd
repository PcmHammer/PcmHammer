# Someday perhaps Uno will happily build to all platforms under one csproj
# Note from Antus: buildall.cmd in the kernels sub dir has been updated to detect and copy to all target paths.
@echo off
set SOURCE=..\..\Kernels
set ARCH32=x86\Debug
set ARCH64=Debug
set UNO_PATH=UnoUI\PcmHacking.UnoUI\bin
echo Source: %SOURCE%
echo Debug: %DEBUG%

set NET_VERSION=net10.0
for %%T in (%ARCH64%\%NET_VERSION%-android %ARCH32%\%NET_VERSION%-windows10.0.26100.0\win-x86) do copy %SOURCE%\*.bin %UNO_PATH%\%%T
copy %SOURCE%\*.bin WindowsForms\PcmHammer\bin\debug
