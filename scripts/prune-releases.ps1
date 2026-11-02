# Preview by default. Publishing scripts opt in with -Apply after a successful upload.
[CmdletBinding()]
param(
    [ValidateSet('Both','GitHub','CNB')][string]$Provider='Both',
    [ValidateRange(1,100)][int]$Keep=3,
    [string]$CnbRepository='https://cnb.cool/railgunhamster/HarnessPortable',
    [switch]$Apply,
    [string]$AuditDirectory=''
)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'ReleaseRetention.ps1')
. (Join-Path $PSScriptRoot 'CnbPublishing.ps1')
if(!$AuditDirectory){$AuditDirectory=Join-Path $root ('artifacts/release-retention/'+[DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))}
[void][IO.Directory]::CreateDirectory($AuditDirectory)
$cnbUri=[Uri]$CnbRepository
$repoPath=$cnbUri.AbsolutePath.Trim('/')
if($cnbUri.Scheme -ne 'https' -or $cnbUri.Host -ne 'cnb.cool' -or $cnbUri.UserInfo -or $cnbUri.Query -or $cnbUri.Fragment -or
    $repoPath.Split('/').Count -lt 2 -or $repoPath -match '/-/'){throw 'Expected a CNB repository root URL.'}
$CnbRepository=$CnbRepository.TrimEnd('/')
$cnbApi='https://api.cnb.cool'+$cnbUri.AbsolutePath.TrimEnd('/')
$githubApi='https://api.github.com/repos/RailgunHamster/HarnessPortable'
function Get-RetentionGitHubToken {
    if($env:GITHUB_TOKEN){return $env:GITHUB_TOKEN}
    if($env:GH_TOKEN){return $env:GH_TOKEN}
    $filled="protocol=https`nhost=github.com`n`n"|git -C $root -c credential.interactive=never credential fill 2>$null
    foreach($line in $filled){if($line -match '^password=(.+)$'){return $Matches[1].Trim()}}
    throw 'GitHub publishing credential unavailable.'
}
function Request-RetentionApi($Path,$Method='Get'){
    Invoke-RestMethod -NoProxy -Uri ($api+$Path) -Headers $headers -Method $Method -TimeoutSec 45
}
function Get-RetentionReleases {
    $all=[Collections.Generic.List[object]]::new()
    $ids=[Collections.Generic.HashSet[string]]::new()
    for($page=1;$page -le 1000;$page++){
        $query=if($currentProvider -eq 'CNB'){"?page=$page&page_size=100"}else{"?page=$page&per_page=100"}
        $batch=Request-RetentionApi ($releasePath+$query)
        foreach($release in $batch){
            if(!$release.id -or !$ids.Add([string]$release.id)){throw 'Incomplete or overlapping release pagination; refusing cleanup.'}
            $all.Add($release)
        }
        if(@($batch).Count -lt 100){return ,$all.ToArray()}
    }
    throw 'Release pagination limit reached; refusing cleanup.'
}
function Get-CnbFeedTags {
    foreach($name in @('releases.win.json','android.json','macos.json')){
        try{$feed=Invoke-RestMethod -NoProxy -Uri ($CnbRepository+'/-/git/raw/updates/'+$name) -TimeoutSec 30}
        catch{if([int]$_.Exception.Response.StatusCode -eq 404){continue};throw}
        $versions=if($name -eq 'releases.win.json'){@($feed.Assets|ForEach-Object Version)}elseif($name -eq 'android.json'){@($feed.versionName)}else{@($feed.version)}
        foreach($version in $versions){
            if($version -notmatch '^\d+\.\d+\.\d+$'){throw "Invalid active update feed: $name"}
            'v'+$version
        }
    }
}
$providers=if($Provider -eq 'Both'){@('GitHub','CNB')}else{@($Provider)}
foreach($currentProvider in $providers){
    if($currentProvider -eq 'CNB'){
        if(!$env:CNB_TOKEN_FILE){$env:CNB_TOKEN_FILE=(& git -C $root config --get cnb.tokenFile 2>$null)}
        $api=$cnbApi;$releasePath='/-/releases'
        $headers=@{Authorization=('Bearer '+(Get-CnbPublishingToken));Accept='application/json'}
    }else{
        $api=$githubApi;$releasePath='/releases'
        $headers=@{Authorization=('Bearer '+(Get-RetentionGitHubToken));Accept='application/vnd.github+json';'User-Agent'='HarnessPortable'}
    }
    $before=Get-RetentionReleases
    $latest=Request-RetentionApi ($releasePath+'/latest')
    $protected=@($latest.tag_name)
    if($currentProvider -eq 'CNB'){$protected+=@(Get-CnbFeedTags)}
    $plan=Get-ReleaseRetentionPlan -Releases $before -Keep $Keep -ProtectedTags $protected
    $record=[ordered]@{provider=$currentProvider;keep=$Keep;apply=[bool]$Apply;protectedTags=$protected;before=$before;plan=$plan;deleted=@()}
    $audit=Join-Path $AuditDirectory ($currentProvider.ToLowerInvariant()+'.json')
    $record|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $audit -Encoding utf8
    Write-Host ("${currentProvider}: keep "+(($plan.Keep|ForEach-Object tag_name)-join ', ')+"; remove "+$plan.Remove.Count+' older releases.')
    if(!$Apply){continue}
    foreach($candidate in $plan.Remove){
        $current=Request-RetentionApi ($releasePath+'/'+$candidate.id)
        if($current.tag_name -cne $candidate.tag_name -or $current.draft -ne $false -or $current.prerelease -ne $false -or
            $current.updated_at -ne $candidate.updated_at -or $current.immutable -eq $true){
            throw ('Release changed or is immutable; refusing cleanup: '+$candidate.tag_name)
        }
        Request-RetentionApi ($releasePath+'/'+$candidate.id) Delete|Out-Null
        $record.deleted+=@($candidate.tag_name)
        $record|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $audit -Encoding utf8
        Write-Host ("${currentProvider}: removed "+$candidate.tag_name)
    }
    $after=Get-RetentionReleases
    foreach($retained in $plan.Keep){
        $remaining=@($after|Where-Object id -eq $retained.id)
        if($remaining.Count -ne 1){throw ('Retained release missing: '+$retained.tag_name)}
        $current=Request-RetentionApi ($releasePath+'/'+$retained.id)
        foreach($asset in $retained.assets){
            $match=@($current.assets|Where-Object name -eq $asset.name)
            if($match.Count -ne 1 -or $match[0].id -ne $asset.id -or $match[0].size -ne $asset.size -or
                $match[0].digest -ne $asset.digest -or $match[0].hash_value -ne $asset.hash_value){
                throw ('Retained release asset changed: '+$retained.tag_name+'/'+$asset.name)
            }
        }
    }
    foreach($removed in $plan.Remove){if($removed.id -in @($after|ForEach-Object id)){throw ('Release still present after deletion: '+$removed.tag_name)}}
    $latestAfter=Request-RetentionApi ($releasePath+'/latest')
    if($latestAfter.id -ne $latest.id){throw 'The latest release changed during cleanup.'}
    $record.after=@($after|ForEach-Object tag_name)
    $record.verified=$true
    $record|ConvertTo-Json -Depth 30|Set-Content -LiteralPath $audit -Encoding utf8
    Write-Host ("${currentProvider}: verified retained releases and their assets.")
}
