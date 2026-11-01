# Upload public versioned assets, verify anonymous downloads, then advance the static update index.
[CmdletBinding()]
param(
    [ValidateSet('All','Windows','Android','macOS')][string]$Platform='All',
    [string]$Version='',
    [string]$Repository='https://cnb.cool/railgunhamster/HarnessPortable'
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'CnbPublishing.ps1')
if(!$env:CNB_TOKEN_FILE){$env:CNB_TOKEN_FILE=(& git -C $root config --get cnb.tokenFile 2>$null)}
if(!$Version){$Version=(Get-Content -LiteralPath (Join-Path $root 'VERSION') -Raw).Trim()}
if($Version -notmatch '^\d+\.\d+\.\d+$'){throw 'Invalid release version.'}
$uri=[Uri]$Repository
if($uri.Scheme -ne 'https' -or $uri.Host -ne 'cnb.cool' -or $uri.UserInfo -or $uri.Query -or $uri.Fragment){throw 'Expected a public CNB repository URL.'}
$repo=$uri.AbsolutePath.Trim('/');if($repo.Split('/').Count -lt 2 -or $repo -match '/-/'){throw 'Use the repository root URL.'}
$Repository='https://cnb.cool/'+$repo
$token=Get-CnbPublishingToken
$headers=@{Authorization="Bearer $token";Accept='application/json'}
$api='https://api.cnb.cool/'+$repo
function Request-Cnb([string]$Path,[string]$Method='Get',$Body=$null){
    $request=@{Uri=($api+$Path);Method=$Method;Headers=$headers;NoProxy=$true;TimeoutSec=45}
    if($null -ne $Body){$request.Body=$Body|ConvertTo-Json -Depth 10 -Compress;$request.ContentType='application/json'}
    Invoke-RestMethod @request
}
function Release-Git { param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)
    $helper='!pwsh -NoProfile -File "'+(Join-Path $PSScriptRoot 'cnb-git-credential.ps1').Replace('\','/')+'"'
    & git -c credential.interactive=never -c credential.https://cnb.cool.helper= -c ('credential.https://cnb.cool.helper='+$helper) -c http.proxy= -c http.version=HTTP/1.1 @Arguments
    if($LASTEXITCODE -ne 0){throw 'CNB release Git operation failed.'}
}
$tag='v'+$Version
$commit=(& git -C $root rev-parse ($tag+'^{}')).Trim()
if($LASTEXITCODE -ne 0){throw 'Create the tested release tag before publication.'}
$metadata=Request-Cnb ''
if($metadata.visibility_level -ne 'Public'){throw 'Update clients require a public CNB repository.'}
Release-Git -C $root push ($Repository+'.git') main ('refs/tags/'+$tag)
$stage=Join-Path $root ('artifacts/cnb-'+$Version)
$verified=Join-Path $stage 'verified'
[void][IO.Directory]::CreateDirectory($verified)
$files=[Collections.Generic.List[string]]::new()
$feeds=[Collections.Generic.List[string]]::new()
$files.Add((Join-Path $root 'LICENSE'));$files.Add((Join-Path $root 'NOTICE'))
if($Platform -in @('All','Windows')){
    $pack=Join-Path $root 'artifacts/velopack'
    $manifest=Get-Content -LiteralPath (Join-Path $pack 'assets.win.json') -Raw|ConvertFrom-Json
    foreach($asset in $manifest){$files.Add((Join-Path $pack $asset.RelativeFileName))}
    $feed=Get-Content -LiteralPath (Join-Path $pack 'releases.win.json') -Raw|ConvertFrom-Json
    $feed.Assets=@($feed.Assets|Where-Object Version -eq $Version)
    if(!($feed.Assets|Where-Object Type -eq 'Full')){throw 'Current Windows full package is missing.'}
    $path=Join-Path $stage 'releases.win.json';[IO.File]::WriteAllText($path,($feed|ConvertTo-Json -Depth 10)+"`n")
    $files.Add($path);$feeds.Add($path)
}
if($Platform -in @('All','Android')){
    $android=Join-Path $root 'artifacts/android';$apk='HarnessPortable-'+$Version+'.apk'
    $feed=Get-Content -LiteralPath (Join-Path $android 'android.json') -Raw|ConvertFrom-Json
    if($feed.versionName -ne $Version){throw 'Android feed version mismatch.'}
    $feed.apk=$Repository+'/-/releases/download/'+$tag+'/'+$apk
    $path=Join-Path $stage 'android.json';[IO.File]::WriteAllText($path,($feed|ConvertTo-Json -Depth 8)+"`n")
    $files.Add((Join-Path $android $apk));$files.Add($path);$feeds.Add($path)
}
if($Platform -eq 'macOS'){
    $mac=Join-Path $root 'artifacts/macos';$feed=Get-Content -LiteralPath (Join-Path $mac 'macos.json') -Raw|ConvertFrom-Json
    if($feed.version -ne $Version){throw 'macOS feed version mismatch.'}
    $dmg=Split-Path -Leaf $feed.dmg;$files.Add((Join-Path $mac $dmg))
    $feed.dmg=$Repository+'/-/releases/download/'+$tag+'/'+$dmg
    $path=Join-Path $stage 'macos.json';[IO.File]::WriteAllText($path,($feed|ConvertTo-Json -Depth 8)+"`n")
    $files.Add($path);$feeds.Add($path)
}
$sums=foreach($file in $files){(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+(Split-Path -Leaf $file)}
$sumPath=Join-Path $stage ('SHA256SUMS.'+$Platform.ToLowerInvariant()+'.txt');[IO.File]::WriteAllLines($sumPath,[string[]]$sums);$files.Add($sumPath)
try{$release=Request-Cnb ('/-/releases/tags/'+$tag)}catch{if([int]$_.Exception.Response.StatusCode -ne 404){throw};$release=$null}
if(!$release){
    $notes=Get-Content -LiteralPath (Join-Path $root 'artifacts/release-notes.md') -Raw
    $release=Request-Cnb '/-/releases' 'Post' @{tag_name=$tag;target_commitish=$commit;name=('Harness Portable '+$Version);body=$notes;draft=$false;prerelease=$true;make_latest='false'}
}
foreach($file in $files){
    $name=Split-Path -Leaf $file;$hash=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    $release=Request-Cnb ('/-/releases/'+$release.id)
    $asset=@($release.assets|Where-Object name -eq $name)
    if($asset.Count -gt 1){throw ('Ambiguous CNB asset: '+$name)}
    if(!$asset.Count){
        Write-Host ('CNB: uploading '+$name)
        $upload=Request-Cnb ('/-/releases/'+$release.id+'/asset-upload-url') 'Post' @{asset_name=$name;size=(Get-Item -LiteralPath $file).Length;overwrite=$false}
        try{Invoke-WebRequest -NoProxy -Method Put -Uri $upload.upload_url -ContentType 'application/octet-stream' -InFile $file -TimeoutSec 180 | Out-Null}
        catch{throw ('CNB asset transfer failed: '+$name+'; HTTP '+[int]$_.Exception.Response.StatusCode)}
        $confirmation=[Uri]$upload.verify_url
        if($confirmation.Scheme -ne 'https' -or $confirmation.Host -notin @('api.cnb.cool','cnb.cool')){throw 'Unexpected CNB verification host.'}
        Invoke-RestMethod -NoProxy -Method Post -Uri $confirmation -Headers $headers -TimeoutSec 45 | Out-Null
    }
    $download=Join-Path $verified $name
    Invoke-WebRequest -NoProxy -Uri ($Repository+'/-/releases/download/'+$tag+'/'+[Uri]::EscapeDataString($name)) -OutFile $download -TimeoutSec 120
    if((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $hash){throw ('CNB anonymous download hash mismatch: '+$name)}
    Write-Host ('CNB: verified '+$name)
}
Request-Cnb ('/-/releases/'+$release.id) 'Patch' @{draft=$false;prerelease=$false;make_latest='true'} | Out-Null
$checkout=Join-Path $root 'artifacts/cnb-feeds'
if(!(Test-Path -LiteralPath (Join-Path $checkout '.git'))){
    [void][IO.Directory]::CreateDirectory($checkout)
    Release-Git -C $checkout init --initial-branch=updates
    Release-Git -C $checkout remote add origin ($Repository+'.git')
}
# Feeds are verified byte-for-byte against their release assets; Git must not rewrite line endings.
Release-Git -C $checkout config core.autocrlf false
$remote=& git -C $checkout -c http.proxy= ls-remote --heads origin refs/heads/updates
if($LASTEXITCODE -ne 0){throw 'Cannot inspect CNB update branch.'}
if($remote){Release-Git -C $checkout fetch origin updates;Release-Git -C $checkout checkout -B updates origin/updates}
foreach($file in $feeds){Copy-Item -LiteralPath $file -Destination $checkout -Force}
Release-Git -C $checkout add -- '*.json'
& git -C $checkout diff --cached --quiet
if($LASTEXITCODE -ne 0){Release-Git -C $checkout commit -m ("Publish $Platform update feeds for $Version");Release-Git -C $checkout push origin HEAD:refs/heads/updates}
foreach($file in $feeds){
    $name=Split-Path -Leaf $file;$path=Join-Path $verified ('public-'+$name)
    Invoke-WebRequest -NoProxy -Uri ($Repository+'/-/git/raw/updates/'+$name+'?verification='+[DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()) -OutFile $path -TimeoutSec 30
    if((Get-FileHash -LiteralPath $path).Hash -ne (Get-FileHash -LiteralPath $file).Hash){throw ('CNB public index hash mismatch: '+$name)}
}
[ordered]@{version=$Version;repository=$Repository;release=($Repository+'/-/releases/tag/'+$tag);anonymousDownloads='verified';feeds=@($feeds|ForEach-Object {Split-Path -Leaf $_})}|ConvertTo-Json|Tee-Object -FilePath (Join-Path $stage ('publication-'+$Platform+'.json'))
