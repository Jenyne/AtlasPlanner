# Publish the public build and install it for daily use on this PC.
# Also refreshes Desktop shortcuts (public always; personal only if hooks exist).
#
# Usage:
#   .\scripts\update-local-release.ps1
#   .\scripts\update-local-release.ps1 -Version 0.5.0
#   .\scripts\update-local-release.ps1 -SkipPersonal

param(
    [string]$Version = "",
    [switch]$SkipPersonal,
    [string]$InstallDir = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

function New-Shortcut([string]$Path, [string]$Target, [string]$WorkDir, [string]$Description) {
    $shell = New-Object -ComObject WScript.Shell
    $sc = $shell.CreateShortcut($Path)
    $sc.TargetPath = $Target
    $sc.WorkingDirectory = $WorkDir
    $sc.Description = $Description
    $sc.Save()
}

& (Join-Path $PSScriptRoot "publish-public.ps1") -Version $Version
if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
    # publish-public throws on failure; keep going only if it returned.
}

if (-not $Version) {
    $props = Get-Content (Join-Path $Root "Directory.Build.props") -Raw
    if ($props -match "<Version>([^<]+)</Version>") {
        $Version = $Matches[1].Trim()
    }
}

if (-not $InstallDir) {
    $InstallDir = Join-Path $env:LOCALAPPDATA "AtlasPlanner\app"
}

$PublicBuild = Join-Path $Root "artifacts\public\$Version"
if (-not (Test-Path $PublicBuild)) {
    throw "Public build folder missing: $PublicBuild"
}

if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
}
New-Item -ItemType Directory -Path $InstallDir | Out-Null
Copy-Item (Join-Path $PublicBuild "*") $InstallDir -Recurse -Force

$PublicExe = Join-Path $InstallDir "AtlasPlanner.Gui.exe"
$Desktop = [Environment]::GetFolderPath("Desktop")
New-Shortcut `
    (Join-Path $Desktop "Atlas Planner.lnk") `
    $PublicExe `
    $InstallDir `
    "Atlas Planner (public — no Send to game)"

Write-Host "Installed public build to: $InstallDir"
Write-Host "Desktop shortcut: Atlas Planner.lnk"

$PersonalHook = Join-Path $Root "src\AtlasPlanner.Gui\Views\MainWindow.Personal.cs"
if (-not $SkipPersonal -and (Test-Path $PersonalHook)) {
    $PersonalDir = Join-Path $Root "artifacts\personal\$Version"
    $Project = Join-Path $Root "src\AtlasPlanner.Gui\AtlasPlanner.Gui.csproj"

    Write-Host "Publishing personal build (local only, not for GitHub Release) ..."
    if (Test-Path $PersonalDir) {
        Remove-Item $PersonalDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PersonalDir | Out-Null

    dotnet publish $Project `
        -c Release `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:AtlasPlannerPersonal=true `
        -p:Version=$Version `
        -o $PersonalDir

    if ($LASTEXITCODE -ne 0) {
        throw "Personal publish failed."
    }

    Get-ChildItem $PersonalDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

    $PersonalExe = Join-Path $PersonalDir "AtlasPlanner.Gui.exe"
    New-Shortcut `
        (Join-Path $Desktop "Atlas Planner (Personal).lnk") `
        $PersonalExe `
        $PersonalDir `
        "Atlas Planner with Send to game — local only, never ship this folder"

    Write-Host "Personal build: $PersonalDir"
    Write-Host "Desktop shortcut: Atlas Planner (Personal).lnk"
}
elseif (-not $SkipPersonal) {
    Write-Host "No personal hooks on disk — skipped personal shortcut."
}

Write-Host ""
Write-Host "Done. Public release zip (for GitHub): artifacts\AtlasPlanner-$Version-win-x64.zip"
Write-Host "Do not upload artifacts\personal\ — that folder is for you only."
