# Build the signed Android APK, write android.json, copy into
# \\server-home\public\Software\HarnessPortable-Releases, and attach the
# APK + feed to the GitHub release for this VERSION.
#
#   pwsh -File scripts/release-android.ps1
param(
    [switch]$SkipGitHub,
    [switch]$SkipShare
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = (Get-Content -Raw (Join-Path $root "VERSION")).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+') {
    throw "VERSION file is missing or invalid: '$version'"
}

function Get-ChangelogSection([string]$changelog, [string]$ver) {
    $lines = $changelog -replace "`r`n", "`n" -split "`n"
    $start = -1
    $escaped = [regex]::Escape($ver)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match "^##\s+\[?$escaped\]?\s*$") {
            $start = $i
            break
        }
    }
    if ($start -lt 0) { return "# Harness Portable $ver" }
    $end = $lines.Length
    for ($i = $start + 1; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^##\s+') { $end = $i; break }
    }
    return (($lines[$start..($end - 1)]) -join "`n").Trim()
}

function Get-GitHubToken {
    $token = $env:GITHUB_TOKEN
    if ([string]::IsNullOrWhiteSpace($token)) { $token = $env:GH_TOKEN }
    if ([string]::IsNullOrWhiteSpace($token)) {
        $filled = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
        foreach ($line in $filled -split "`n") {
            if ($line -match '^password=(.+)$') { $token = $Matches[1].Trim(); break }
        }
    }
    return $token
}

$changelog = Get-Content -Raw (Join-Path $root "CHANGELOG.md")
$notes = Get-ChangelogSection $changelog $version
$share = "\\server-home\public\Software\HarnessPortable-Releases"
$artifacts = Join-Path $root "artifacts\android"
New-Item -ItemType Directory -Force $artifacts | Out-Null

if (-not $env:JAVA_HOME -or -not (Test-Path $env:JAVA_HOME)) {
    $candidate = "C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot"
    if (Test-Path $candidate) { $env:JAVA_HOME = $candidate }
}
if ($env:JAVA_HOME) {
    $env:Path = "$env:JAVA_HOME\bin;" + $env:Path
}

Write-Host "Building Android release $version..."
& (Join-Path $root "gradlew.bat") :app:assembleRelease --quiet
if ($LASTEXITCODE -ne 0) { throw "assembleRelease failed" }

$built = Join-Path $root "app\build\outputs\apk\release\app-release.apk"
if (-not (Test-Path $built)) { throw "APK missing: $built" }

# versionCode is the integer in app/build.gradle.kts
$gradle = Get-Content (Join-Path $root "app\build.gradle.kts") -Raw
if ($gradle -notmatch 'versionCode\s*=\s*(\d+)') {
    throw "Could not read versionCode from app/build.gradle.kts"
}
$versionCode = [int]$Matches[1]

$apkName = "HarnessPortable-$version.apk"
$apkDest = Join-Path $artifacts $apkName
Copy-Item $built $apkDest -Force

$feed = [ordered]@{
    versionCode = $versionCode
    versionName = $version
    apk         = $apkName
    notes       = $notes
}
$feedJson = $feed | ConvertTo-Json -Compress
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $artifacts "android.json"), $feedJson, $utf8)

if (-not $SkipShare) {
    Write-Host "Copying APK feed to $share"
    New-Item -ItemType Directory -Force $share | Out-Null
    Copy-Item $apkDest (Join-Path $share $apkName) -Force
    Copy-Item (Join-Path $artifacts "android.json") (Join-Path $share "android.json") -Force
    Copy-Item $apkDest (Join-Path $share "HarnessPortable-android.apk") -Force
}

if (-not $SkipGitHub) {
    $token = Get-GitHubToken
    if ([string]::IsNullOrWhiteSpace($token)) {
        Write-Warning "No GitHub token. Skipping GitHub APK upload."
    } else {
        $headers = @{
            Authorization = "Bearer $token"
            "User-Agent"  = "HarnessPortable"
            Accept        = "application/vnd.github+json"
        }
        $tag = "v$version"
        $rel = $null
        try {
            $rel = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/RailgunHamster/HarnessPortable/releases/tags/$tag"
        } catch {
            Write-Host "Creating GitHub release $tag..."
            $body = @{
                tag_name = $tag
                name     = "Harness Portable $version"
                body     = $notes
                draft    = $false
                prerelease = $false
            } | ConvertTo-Json
            $rel = Invoke-RestMethod -Method POST -Headers $headers -ContentType "application/json" -Body $body `
                -Uri "https://api.github.com/repos/RailgunHamster/HarnessPortable/releases"
        }
        $uploadBase = ($rel.upload_url -replace '\{.*}', '')
        foreach ($file in @(
            @{ Path = $apkDest; Name = $apkName; Type = "application/vnd.android.package-archive" },
            @{ Path = (Join-Path $artifacts "android.json"); Name = "android.json"; Type = "application/json" }
        )) {
            $existing = @($rel.assets) | Where-Object { $_.name -eq $file.Name }
            foreach ($asset in $existing) {
                Write-Host "Replacing GitHub asset $($file.Name)..."
                Invoke-RestMethod -Method DELETE -Headers $headers -Uri $asset.url | Out-Null
            }
            Write-Host "Uploading GitHub asset $($file.Name)..."
            $uploadHeaders = @{
                Authorization = "Bearer $token"
                "User-Agent"  = "HarnessPortable"
                Accept        = "application/vnd.github+json"
            }
            Invoke-WebRequest -Method POST -Headers $uploadHeaders `
                -ContentType $file.Type -InFile $file.Path `
                -Uri "$uploadBase?name=$($file.Name)" | Out-Null
        }
    }
}

Write-Host "Done. Android $version (versionCode $versionCode)"
Write-Host "  apk   $apkDest"
if (-not $SkipShare) { Write-Host "  share $share" }
