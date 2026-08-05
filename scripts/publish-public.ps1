# Publishes the Atlas Planner GUI as a self-contained Windows zip (public-safe build).
# Usage (from the AtlasPlanner repo root):
#   .\scripts\publish-public.ps1
#   .\scripts\publish-public.ps1 -Version 0.5.0

param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

if (-not $Version) {
    $props = Get-Content (Join-Path $Root "Directory.Build.props") -Raw
    if ($props -match "<Version>([^<]+)</Version>") {
        $Version = $Matches[1].Trim()
    }
    else {
        throw "Could not read <Version> from Directory.Build.props. Pass -Version explicitly."
    }
}

$OutDir = Join-Path $Root "artifacts\public\$Version"
$ZipPath = Join-Path $Root "artifacts\AtlasPlanner-$Version-win-x64.zip"
$Project = Join-Path $Root "src\AtlasPlanner.Gui\AtlasPlanner.Gui.csproj"

Write-Host "Publishing Atlas Planner $Version (public) ..."

if (Test-Path $OutDir) {
    Remove-Item $OutDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutDir | Out-Null

# *.Personal.cs is excluded by the csproj unless AtlasPlannerPersonal=true.
# AtlasPlannerPublic=true is an extra guard for the release zip.
dotnet publish $Project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:AtlasPlannerPublic=true `
    -p:Version=$Version `
    -o $OutDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed."
}

Get-ChildItem $OutDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Compress-Archive -Path (Join-Path $OutDir "*") -DestinationPath $ZipPath -Force

Write-Host ""
Write-Host "Build ready:"
Write-Host "  folder: $OutDir"
Write-Host "  zip:    $ZipPath"
Write-Host ""
Write-Host "Attach that zip to a GitHub Release for tag v$Version."
