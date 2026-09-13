# Pack Harness Portable with Velopack, copy the feed to the LAN share, and
# optionally publish a GitHub release. Version comes from the repo VERSION file.
#
#   pwsh -File scripts/release-windows.ps1
#   pwsh -File scripts/release-windows.ps1 -SkipGitHub
#   pwsh -File scripts/release-windows.ps1 -SkipShare
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
    if ($start -lt 0) {
        return "# Harness Portable $ver"
    }
    $end = $lines.Length
    for ($i = $start + 1; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^##\s+') {
            $end = $i
            break
        }
    }
    return (($lines[$start..($end - 1)]) -join "`n").Trim()
}

$changelog = Get-Content -Raw (Join-Path $root "CHANGELOG.md")
$notes = Get-ChangelogSection $changelog $version

$publishDir = Join-Path $root "artifacts\win-publish"
$packDir = Join-Path $root "artifacts\velopack"
$notesFile = Join-Path $root "artifacts\release-notes.md"
$share = "\\server-home\public\Software\HarnessPortable-Releases"
$icon = Join-Path $root "windows\HarnessPortable.Windows\Assets\HarnessPortable.ico"

New-Item -ItemType Directory -Force (Join-Path $root "artifacts") | Out-Null
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Force $publishDir | Out-Null
New-Item -ItemType Directory -Force $packDir | Out-Null
Set-Content -Path $notesFile -Value $notes -Encoding utf8

Write-Host "Publishing Windows build $version..."
dotnet publish (Join-Path $root "windows\HarnessPortable.Windows\HarnessPortable.Windows.csproj") `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if (-not $SkipShare -and (Test-Path $share)) {
    Write-Host "Seeding pack output from $share so Velopack can build a delta..."
    Get-ChildItem $share -Filter "*.nupkg" -ErrorAction SilentlyContinue |
        Copy-Item -Destination $packDir -Force
    foreach ($name in @("releases.win.json", "RELEASES", "release-history.json", "assets.win.json")) {
        $from = Join-Path $share $name
        if (Test-Path $from) {
            Copy-Item $from $packDir -Force
        }
    }
}

Write-Host "Packing Velopack release..."
vpk pack `
    --yes `
    --packId HarnessPortable `
    --packVersion $version `
    --packDir $publishDir `
    --mainExe HarnessPortable.exe `
    --outputDir $packDir `
    --releaseNotes $notesFile `
    --icon $icon `
    --packTitle "Harness Portable" `
    --packAuthors Harness `
    --framework webview2
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

if (-not $SkipShare) {
    Write-Host "Uploading feed to $share"
    New-Item -ItemType Directory -Force $share | Out-Null
    vpk upload local --yes --outputDir $packDir --path $share --keepMaxReleases 30
    if ($LASTEXITCODE -ne 0) { throw "vpk upload local failed" }
}

if (-not $SkipGitHub) {
    $token = $env:GITHUB_TOKEN
    if ([string]::IsNullOrWhiteSpace($token)) { $token = $env:GH_TOKEN }
    if ([string]::IsNullOrWhiteSpace($token)) {
        $filled = "protocol=https`nhost=github.com`n`n" | git credential fill 2>$null
        foreach ($line in $filled -split "`n") {
            if ($line -match '^password=(.+)$') { $token = $Matches[1].Trim(); break }
        }
    }
    if ([string]::IsNullOrWhiteSpace($token)) {
        Write-Warning "No GitHub token (GITHUB_TOKEN / GH_TOKEN / git credential). Skipping GitHub upload."
    } else {
        Write-Host "Uploading GitHub release v$version..."
        vpk upload github --yes `
            --outputDir $packDir `
            --repoUrl https://github.com/RailgunHamster/HarnessPortable `
            --token $token `
            --publish true `
            --merge true `
            --tag "v$version" `
            --releaseName "Harness Portable $version"
        if ($LASTEXITCODE -ne 0) { throw "vpk upload github failed" }
    }
}

Write-Host "Done. Version $version"
Write-Host "  pack   $packDir"
if (-not $SkipShare) { Write-Host "  share  $share" }
Write-Host "  setup  $(Join-Path $packDir 'HarnessPortable-win-Setup.exe')"
