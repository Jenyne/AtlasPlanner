@echo off
REM Public Atlas Planner (no Send to game). Double-click or run from a shell.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run-public.ps1"
if errorlevel 1 pause
