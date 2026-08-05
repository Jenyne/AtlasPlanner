@echo off
REM Personal Atlas Planner (Send to game). Local only — never ship this build.
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run-personal.ps1"
if errorlevel 1 pause
