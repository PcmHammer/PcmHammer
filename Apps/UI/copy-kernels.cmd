@echo off
set SOURCE=..\..\Kernels
set UNO_DEBUG=UnoUI\PcmHacking.Uno\bin\debug
echo Source: %SOURCE%
echo Debug: %DEBUG%

for %%T in (net8.0 net8.0-android net8.0-desktop net8.0-ios net8.0-maccatalyst net8.0-windows10.0.26100) do copy %SOURCE%\*.bin %UNO_DEBUG%\%%T
copy %SOURCE%\*.bin WindowsForms\PcmHammer\bin\debug
