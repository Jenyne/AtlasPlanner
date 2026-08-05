# Build and run the personal Atlas Planner (Send to game), if personal hooks exist.
# Never ships in the public release zip. Usage: .\scripts\run-personal.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

$PersonalHook = Join-Path $Root "src\AtlasPlanner.Gui\Views\MainWindow.Personal.cs"
if (-not (Test-Path $PersonalHook)) {
    Write-Error @"
Personal hooks not found ($PersonalHook).
This machine has no local Send-to-game files. Use run-public.ps1 instead.
"@
    exit 1
}

$Project = Join-Path $Root "src\AtlasPlanner.Gui\AtlasPlanner.Gui.csproj"
$OutDir = Join-Path $Root "artifacts\run\personal"

Write-Host "Building personal Atlas Planner (Send to game) ..."
dotnet build $Project -c Debug -p:AtlasPlannerPersonal=true -o $OutDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $OutDir "AtlasPlanner.Gui.exe"
Write-Host "Starting $exe"
Start-Process $exe -WorkingDirectory $OutDir
