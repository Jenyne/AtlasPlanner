# Publishes the public Atlas Planner GUI (no "Send to game") as a self-contained zip.
# Usage (from the AtlasPlanner repo root):
#   .\scripts\publish-public.ps1
#   .\scripts\publish-public.ps1 -Version 0.4.0

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

Write-Host "Publishing public Atlas Planner $Version ..."

if (Test-Path $OutDir) {
    Remove-Item $OutDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutDir | Out-Null

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

# Drop noisy PDB from the zip users download.
Get-ChildItem $OutDir -Filter *.pdb -ErrorAction SilentlyContinue | Remove-Item -Force

if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Compress-Archive -Path (Join-Path $OutDir "*") -DestinationPath $ZipPath -Force

Write-Host ""
Write-Host "Public build ready:"
Write-Host "  folder: $OutDir"
Write-Host "  zip:    $ZipPath"
Write-Host ""
Write-Host "Create a GitHub Release with tag v$Version and attach that zip."
