# Mirror built packages to Gitee and advance the public static feeds only after downloads verify.
param(
    [ValidateSet('All','Windows','Android','macOS')][string]$Platform = 'All',
    [string]$Version = '',
    [string]$Repository = 'https://gitee.com/railgunhamster/HarnessPortable'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$Version) { $Version = (Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim() }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid release version' }
$tag = 'v' + $Version
$repoUri = [Uri]$Repository
$repoParts = $repoUri.AbsolutePath.Trim('/').Split('/')
if ($repoUri.Host -ne 'gitee.com' -or $repoParts.Count -ne 2) { throw 'Expected a Gitee repository URL' }
$Repository = $Repository.TrimEnd('/')
$api = 'https://gitee.com/api/v5/repos/' + ($repoParts -join '/')
$token = $env:GITEE_TOKEN
if (!$token) {
    $credential = "protocol=https`nhost=gitee.com`n`n" | git -c credential.interactive=never credential fill 2>$null
    foreach ($line in $credential) { if ($line -match '^password=(.+)$') { $token = $Matches[1]; break } }
}
if (!$token) { throw 'Set GITEE_TOKEN or save a Gitee credential in Git Credential Manager' }
$headers = @{Authorization="Bearer $token"}
function Invoke-Gitee([string]$Path, [string]$Method='Get', $Body=$null) {
    if ($Method -eq 'Get') { $Path += $(if ($Path.Contains('?')) {'&'} else {'?'}) + 'verification=' + [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() }
    $request = @{Uri=($api+$Path);Headers=$headers;Method=$Method;TimeoutSec=60}
    if ($null -ne $Body) { $request.Body=$Body; $request.ContentType='application/x-www-form-urlencoded' }
    Invoke-RestMethod @request
}
function Invoke-ReleaseGit { param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
    & git -c credential.interactive=never @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('git failed: ' + ($Arguments -join ' ')) }
}
$commit = (& git -C $root rev-parse ($tag+'^{}')).Trim()
if ($LASTEXITCODE -ne 0) { throw "Create tag $tag before publishing" }
# The mirror receives code and tags too; no force push and no credentials in the remote URL.
Invoke-ReleaseGit @('-C',$root,'push',($Repository+'.git'),'main',('refs/tags/'+$tag))
$stage = Join-Path $root ('artifacts/gitee-'+$Version)
$verify = Join-Path $stage 'verified'
New-Item -ItemType Directory -Path $stage,$verify -Force | Out-Null
$files = [System.Collections.Generic.List[string]]::new()
$files.Add((Join-Path $root 'LICENSE')); $files.Add((Join-Path $root 'NOTICE'))
$feeds = [System.Collections.Generic.List[string]]::new()
if ($Platform -in @('All','Windows')) {
    $pack = Join-Path $root 'artifacts/velopack'
    $manifest = Get-Content -LiteralPath (Join-Path $pack 'assets.win.json') -Raw | ConvertFrom-Json
    foreach ($asset in $manifest) { $files.Add((Join-Path $pack $asset.RelativeFileName)) }
    $feed = Get-Content -LiteralPath (Join-Path $pack 'releases.win.json') -Raw | ConvertFrom-Json
    $feed.Assets = @($feed.Assets | Where-Object { $_.Version -eq $Version })
    if (!($feed.Assets | Where-Object Type -eq 'Full')) { throw 'Windows full package is missing from the feed' }
    $feedPath = Join-Path $stage 'releases.win.json'
    $feedText = ($feed | ConvertTo-Json -Depth 8).Replace("`r`n", "`n") + "`n"
    [IO.File]::WriteAllText($feedPath, $feedText)
    $files.Add($feedPath); $feeds.Add($feedPath)
}
if ($Platform -in @('All','Android')) {
    $android = Join-Path $root 'artifacts/android'
    $apk = 'HarnessPortable-'+$Version+'.apk'
    $feed = Get-Content -LiteralPath (Join-Path $android 'android.json') -Raw | ConvertFrom-Json
    if ($feed.versionName -ne $Version) { throw 'Android feed version does not match' }
    $feed.apk = $Repository+'/releases/download/'+$tag+'/'+$apk
    $feedPath = Join-Path $stage 'android.json'
    $feedText = ($feed | ConvertTo-Json -Depth 5).Replace("`r`n", "`n") + "`n"
    [IO.File]::WriteAllText($feedPath, $feedText)
    $files.Add((Join-Path $android $apk)); $files.Add($feedPath); $feeds.Add($feedPath)
}
if ($Platform -eq 'macOS') {
    $mac = Join-Path $root 'artifacts/macos'
    $feed = Get-Content -LiteralPath (Join-Path $mac 'macos.json') -Raw | ConvertFrom-Json
    if ($feed.version -ne $Version) { throw 'macOS feed version does not match' }
    $dmg = Split-Path -Leaf $feed.dmg
    $files.Add((Join-Path $mac $dmg))
    $feed.dmg = $Repository+'/releases/download/'+$tag+'/'+$dmg
    $feedPath = Join-Path $stage 'macos.json'
    $feedText = ($feed | ConvertTo-Json -Depth 5).Replace("`r`n", "`n") + "`n"
    [IO.File]::WriteAllText($feedPath, $feedText)
    $files.Add($feedPath); $feeds.Add($feedPath)
}
foreach ($file in $files) { if (!(Test-Path -LiteralPath $file)) { throw "Missing release file: $file" } }
$release = $null
try { $release = Invoke-Gitee ('/releases/tags/'+$tag) } catch { if ([int]$_.Exception.Response.StatusCode -ne 404) { throw } }
if (!$release) {
    $notes = Get-Content -LiteralPath (Join-Path $root 'artifacts/release-notes.md') -Raw
    $release = Invoke-Gitee '/releases' 'Post' @{tag_name=$tag;name=('Harness Portable '+$Version);body=$notes;target_commitish=$commit;prerelease='true'}
}
$attachments = Invoke-Gitee ('/releases/'+$release.id+'/attach_files?per_page=100')
foreach ($file in $files) {
    $name = Split-Path -Leaf $file
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    $existing = $attachments | Where-Object name -eq $name | Select-Object -First 1
    if (!$existing) {
        Write-Host "Gitee: uploading $name"
        $uploaded = Invoke-RestMethod -Method Post -Uri ($api+'/releases/'+$release.id+'/attach_files') -NoProxy -Headers $headers -Form @{file=(Get-Item -LiteralPath $file)} -TimeoutSec 180
        $existing = $uploaded
    }
    $download = Join-Path $verify $name
    # Deliberately anonymous: published update clients never receive the maintainer token.
    Invoke-WebRequest -Uri $existing.browser_download_url -OutFile $download -TimeoutSec 120
    if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $hash) {
        throw "Gitee download checksum mismatch for $name. Existing releases are not overwritten."
    }
    Write-Host "Gitee: verified $name"
}
Invoke-Gitee ('/releases/'+$release.id) 'Patch' @{name=$release.name;tag_name=$tag;body=$release.body;prerelease='false'} | Out-Null
# A separate tiny branch is the stable index; binaries remain versioned release attachments.
$feedCheckout = Join-Path $root 'artifacts/gitee-feeds'
if (!(Test-Path -LiteralPath (Join-Path $feedCheckout '.git'))) {
    New-Item -ItemType Directory -Path $feedCheckout -Force | Out-Null
    Invoke-ReleaseGit @('-C',$feedCheckout,'init','--initial-branch=updates')
    Invoke-ReleaseGit @('-C',$feedCheckout,'remote','add','origin',($Repository+'.git'))
}
$remoteFeed = & git -C $feedCheckout -c credential.interactive=never ls-remote --heads origin refs/heads/updates
if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Gitee update branch' }
if ($remoteFeed) {
    Invoke-ReleaseGit @('-C',$feedCheckout,'fetch','origin','updates')
    Invoke-ReleaseGit @('-C',$feedCheckout,'checkout','-B','updates','origin/updates')
}
foreach ($file in $feeds) { Copy-Item -LiteralPath $file -Destination $feedCheckout -Force }
Invoke-ReleaseGit @('-C',$feedCheckout,'add','--','*.json')
& git -C $feedCheckout diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    Invoke-ReleaseGit @('-C',$feedCheckout,'commit','-m',("Publish $Platform update feeds for $Version"))
    Invoke-ReleaseGit @('-C',$feedCheckout,'push','origin','HEAD:refs/heads/updates')
}
foreach ($file in $feeds) {
    $name = Split-Path -Leaf $file
    $download = Join-Path $verify ('public-'+$name)
    Invoke-WebRequest -Uri ($Repository+'/raw/updates/'+$name+'?verify='+[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()) -OutFile $download -TimeoutSec 30
    if ((Get-FileHash -LiteralPath $download).Hash -ne (Get-FileHash -LiteralPath $file).Hash) { throw "Public feed checksum mismatch: $name" }
}
[ordered]@{version=$Version;repository=$Repository;release=($Repository+'/releases/tag/'+$tag);publicDownloads='verified';feeds=@($feeds|ForEach-Object {Split-Path -Leaf $_})} | ConvertTo-Json | Tee-Object -FilePath (Join-Path $stage ('publication-'+$Platform+'.json'))
