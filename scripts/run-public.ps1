# Build and run the public Atlas Planner (no Send to game).
# Usage: .\scripts\run-public.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$Project = Join-Path $Root "src\AtlasPlanner.Gui\AtlasPlanner.Gui.csproj"
$OutDir = Join-Path $Root "artifacts\run\public"

Write-Host "Building public Atlas Planner ..."
dotnet build $Project -c Debug -p:AtlasPlannerPublic=true -o $OutDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $OutDir "AtlasPlanner.Gui.exe"
Write-Host "Starting $exe"
Start-Process $exe -WorkingDirectory $OutDir
