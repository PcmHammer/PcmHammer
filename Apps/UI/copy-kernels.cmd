@echo off
set SOURCE=..\..\Kernels
set UNO_DEBUG=UnoUI\PcmHacking.UnoUI\bin\debug
echo Source: %SOURCE%
echo Debug: %DEBUG%

set NET_VERSION=net10.0
for %%T in ("" android desktop ios maccatalyst windows10.0.26100.0) do copy %SOURCE%\*.bin %UNO_DEBUG%\%NET_VERSION%-%%T
copy %SOURCE%\*.bin WindowsForms\PcmHammer\bin\debug
