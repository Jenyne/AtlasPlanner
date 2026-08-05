@echo off
REM Rebuild the public release zip, install it under LocalAppData, refresh Desktop shortcuts.
REM Optional: update-local-release.bat 0.5.0
cd /d "%~dp0"
if "%~1"=="" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\update-local-release.ps1"
) else (
  powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\update-local-release.ps1" -Version "%~1"
)
if errorlevel 1 pause
pause
