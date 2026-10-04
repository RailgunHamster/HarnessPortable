# Build the macOS app on the remote Mac (SSH), package it into an
# ad-hoc signed dmg, write the macos.json update feed, pull the artifacts
# back into artifacts\macos\, and (unless skipped) publish them to the LAN
# share and the GitHub release for this VERSION.
#
#   pwsh -File scripts/release-macos.ps1
#
# Requirements on the Mac (see docs/mac-setup.md): full Xcode, xcodegen,
# Remote Login with the harness_mac_ed25519 key installed.
param(
    [string]$MacTarget = "RailgunHamster@macair",
    [string]$Version = "",
    [switch]$SkipGitHub,
    [switch]$SkipShare
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

if (-not $Version) {
    $Version = (Get-Content -Raw (Join-Path $root "VERSION")).Trim()
}
if ($Version -notmatch '^\d+\.\d+\.\d+') {
    throw "VERSION file is missing or invalid: '$Version'"
}

$key = "C:\Windows\System32\config\systemprofile\.ssh\harness_mac_ed25519"
if (-not (Test-Path $key)) { throw "SSH key not found: $key" }

$sshArgs = @("-i", $key, "-o", "IdentitiesOnly=yes")
$remoteRoot = "harness-portable"

function Invoke-OnMac([string]$command) {
    ssh @sshArgs $MacTarget $command
    if ($LASTEXITCODE -ne 0) { throw "remote command failed ($LASTEXITCODE): $command" }
}

Write-Host "==> syncing sources to $MacTarget"
# tar keeps the transfer single-shot and mirrors deletions of stale files.
Invoke-OnMac "mkdir -p ~/$remoteRoot"
$localTar = Join-Path $env:TEMP "hp-macos-src.tgz"
if (Test-Path $localTar) { Remove-Item $localTar -Force }
tar -czf $localTar -C $root macos shared VERSION
scp @sshArgs $localTar "${MacTarget}:~/$remoteRoot/macos-src.tgz"
if ($LASTEXITCODE -ne 0) { throw "scp upload failed" }

Write-Host "==> generating project + building (Release)"
Invoke-OnMac @"
set -o pipefail
cd ~/$remoteRoot &&
tar -xzf macos-src.tgz &&
cd macos &&
export PATH="/opt/homebrew/bin:`$PATH" &&
xcodegen generate &&
xcodebuild -project HarnessPortable.xcodeproj -scheme HarnessPortable \
  -configuration Release -destination 'platform=macOS' \
  -derivedDataPath build build 2>&1 | tail -30
"@

Write-Host "==> ad-hoc signing + dmg"
Invoke-OnMac @"
cd ~/$remoteRoot/macos &&
APP=build/Build/Products/Release/HarnessPortable.app &&
codesign --force --deep --sign - "`$APP" &&
rm -rf dmgroot && mkdir -p dmgroot &&
cp -R "`$APP" dmgroot/ &&
ln -sfn /Applications dmgroot/Applications &&
hdiutil create -volname "Harness Portable" \
  -srcfolder dmgroot -ov -format UDZO \
  "HarnessPortable-$Version.dmg" &&
shasum -a 256 "HarnessPortable-$Version.dmg"
"@

Write-Host "==> pulling artifact back"
$outDir = Join-Path $root "artifacts\macos"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$dmgName = "HarnessPortable-$Version.dmg"
$dmgPath = Join-Path $outDir $dmgName
scp @sshArgs "${MacTarget}:~/$remoteRoot/macos/$dmgName" "$outDir\"
if ($LASTEXITCODE -ne 0) { throw "scp download failed" }

# Drop the build products and dmg staging folder so Spotlight/Launchpad do not
# index stale app copies next to the real install (they are regenerated on
# every run anyway).
Invoke-OnMac "cd ~/$remoteRoot/macos && rm -rf build dmgroot macos-src.tgz"

Write-Host "==> writing macos.json feed"
$hash = (Get-FileHash $dmgPath -Algorithm SHA256).Hash.ToLowerInvariant()

function Get-ChangelogSection([string]$changelog, [string]$ver) {
    $lines = $changelog -replace "`r`n", "`n" -split "`n"
    $start = -1
    $escaped = [regex]::Escape($ver)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match "^##\s+\[?$escaped\]?\s*$") { $start = $i; break }
    }
    if ($start -lt 0) { return "# Harness Portable $ver" }
    $end = $lines.Length
    for ($i = $start + 1; $i -lt $lines.Length; $i++) {
        if ($lines[$i] -match '^##\s+') { $end = $i; break }
    }
    return (($lines[$start..($end - 1)]) -join "`n").Trim()
}

$notes = Get-ChangelogSection (Get-Content -Raw (Join-Path $root "CHANGELOG.md")) $Version
$feed = [ordered]@{
    version = $Version
    dmg     = $dmgName
    sha256  = $hash
    notes   = $notes
}
$feedJson = $feed | ConvertTo-Json -Compress
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText((Join-Path $outDir "macos.json"), $feedJson, $utf8)

$share = "\\server-home\public\Software\HarnessPortable-Releases"
if (-not $SkipShare) {
    Write-Host "==> copying dmg + feed to $share"
    New-Item -ItemType Directory -Force -Path $share | Out-Null
    Copy-Item $dmgPath (Join-Path $share $dmgName) -Force
    Copy-Item (Join-Path $outDir "macos.json") (Join-Path $share "macos.json") -Force
    Copy-Item $dmgPath (Join-Path $share "HarnessPortable-macos.dmg") -Force
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
        Write-Warning "No GitHub token. Skipping GitHub dmg upload."
    } else {
        Write-Host "==> attaching dmg + feed to GitHub release v$Version"
        $headers = @{
            Authorization = "Bearer $token"
            "User-Agent"  = "HarnessPortable"
            Accept        = "application/vnd.github+json"
        }
        $tag = "v$Version"
        $rel = $null
        try {
            $rel = Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/RailgunHamster/HarnessPortable/releases/tags/$tag"
        } catch {
            $body = @{
                tag_name   = $tag
                name       = "Harness Portable $Version"
                body       = $notes
                draft      = $false
                prerelease = $false
            } | ConvertTo-Json
            $rel = Invoke-RestMethod -Method POST -Headers $headers -ContentType "application/json" -Body $body `
                -Uri "https://api.github.com/repos/RailgunHamster/HarnessPortable/releases"
        }
        $uploadBase = ([string]$rel.upload_url).Split('{')[0].Trim()
        foreach ($file in @(
            @{ Path = $dmgPath; Name = $dmgName; Type = "application/x-apple-diskimage" },
            @{ Path = (Join-Path $outDir "macos.json"); Name = "macos.json"; Type = "application/json" }
        )) {
            $existing = Invoke-RestMethod -Headers $headers `
                -Uri "https://api.github.com/repos/RailgunHamster/HarnessPortable/releases/$($rel.id)/assets"
            $existing | Where-Object { $_.name -eq $file.Name } | ForEach-Object {
                Invoke-RestMethod -Method DELETE -Headers $headers `
                    -Uri "https://api.github.com/repos/RailgunHamster/HarnessPortable/releases/assets/$($_.id)" | Out-Null
            }
            Invoke-RestMethod -Method POST -Headers $headers `
                -ContentType $file.Type `
                -InFile $file.Path `
                -Uri "${uploadBase}?name=$([uri]::EscapeDataString($file.Name))" | Out-Null
        }
    }
}

Write-Host "Done: $dmgPath (+ macos.json feed)"
