@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Install.ps1" -ShowWizard
if errorlevel 1 (
  echo Installation failed. Read the message above.
  pause
  exit /b 1
)
