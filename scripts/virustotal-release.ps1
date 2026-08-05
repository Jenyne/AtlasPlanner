# Upload a release zip to VirusTotal and append the report link to the GitHub release notes.
#
# Usage (PowerShell):
#   $env:VT_API_KEY = 'your-key-here'
#   cd E:\WoW\ExileApi-Compiled-3.26.0.0.1\AtlasPlanner
#   .\scripts\virustotal-release.ps1 -Tag v0.4.0

param(
    [Parameter(Mandatory = $true)][string]$Tag,
    [string]$Repo = "Jenyne/AtlasPlanner",
    [string]$Zip = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:VT_API_KEY)) {
    throw "Set VT_API_KEY first. Example: `$env:VT_API_KEY = 'your-key-here'"
}

if ([string]::IsNullOrWhiteSpace($Zip)) {
    if ($Tag -notmatch '^v(.+)$') {
        throw "Tag must look like v0.4.0"
    }
    $version = $Matches[1]
    $Zip = Join-Path (Split-Path -Parent $PSScriptRoot) ("artifacts\AtlasPlanner-{0}-win-x64.zip" -f $version)
}

if (-not (Test-Path -LiteralPath $Zip)) {
    throw "Zip not found: $Zip"
}

$Zip = (Resolve-Path -LiteralPath $Zip).Path
$fileName = Split-Path -Leaf $Zip
$fileSize = (Get-Item -LiteralPath $Zip).Length
$sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $Zip).Hash.ToLowerInvariant()
$vtUrl = "https://www.virustotal.com/gui/file/$sha256"

Write-Host ("Uploading {0} ({1:N1} MB) to VirusTotal..." -f $fileName, ($fileSize / 1MB))

# Prefer curl.exe — reliable multipart on Windows PowerShell 5.1 and PowerShell 7.
$curl = Get-Command curl.exe -ErrorAction SilentlyContinue
if (-not $curl) {
    throw "curl.exe not found. Install Git for Windows or use Windows 10+ curl."
}

$headers = @{ "x-apikey" = $env:VT_API_KEY }
if ($fileSize -ge 32MB) {
    $upload = Invoke-RestMethod -Method Get -Uri "https://www.virustotal.com/api/v3/files/upload_url" -Headers $headers
    $target = [string]$upload.data
}
else {
    $target = "https://www.virustotal.com/api/v3/files"
}

$tmp = Join-Path $env:TEMP ("vt-upload-{0}.json" -f [guid]::NewGuid().ToString("n"))
try {
    & curl.exe -sS --fail -X POST "$target" `
        -H ("x-apikey: {0}" -f $env:VT_API_KEY) `
        -F ("file=@{0}" -f $Zip) `
        -o $tmp
    if ($LASTEXITCODE -ne 0) {
        throw "VirusTotal upload failed (curl exit $LASTEXITCODE)."
    }
    $response = Get-Content -LiteralPath $tmp -Raw | ConvertFrom-Json
}
finally {
    Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
}

$analysisId = [string]$response.data.id
Write-Host "VirusTotal: $vtUrl"
Write-Host "Analysis id: $analysisId"

$releaseJson = gh api ("repos/{0}/releases/tags/{1}" -f $Repo, $Tag)
$release = $releaseJson | ConvertFrom-Json
$body = [string]$release.body
if ($null -eq $body) { $body = "" }

if ($body -notlike "*$vtUrl*") {
    if ($body.Length -gt 0 -and -not $body.EndsWith("`n")) {
        $body += "`n"
    }
    $body += "`n### VirusTotal`n"
    $body += ("- [{0}]({1})`n" -f $fileName, $vtUrl)

    $bodyFile = Join-Path $env:TEMP ("vt-release-body-{0}.txt" -f [guid]::NewGuid().ToString("n"))
    try {
        Set-Content -LiteralPath $bodyFile -Value $body -Encoding utf8
        gh api ("repos/{0}/releases/{1}" -f $Repo, $release.id) -X PATCH -F ("body=@{0}" -f $bodyFile) | Out-Null
    }
    finally {
        Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Updated release notes for $Tag."
}
else {
    Write-Host "Release notes already contain the VirusTotal link."
}

Write-Host $release.html_url
