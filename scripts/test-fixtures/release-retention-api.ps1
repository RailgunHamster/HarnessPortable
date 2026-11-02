# Exercise the real runner against in-memory API responses; no network or real credentials.
$ErrorActionPreference='Stop'
$root=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runner=Join-Path $root 'scripts/prune-releases.ps1'
$auditRoot=Join-Path $root ('artifacts/test-retention/'+[guid]::NewGuid().ToString('N'))
$previousGitHub=$env:GITHUB_TOKEN;$previousCnb=$env:CNB_TOKEN;$previousTokenFile=$env:CNB_TOKEN_FILE
$env:GITHUB_TOKEN='fixture';$env:CNB_TOKEN='fixture';$env:CNB_TOKEN_FILE='fixture-unused'
function New-Fixture($Version){
    [pscustomobject]@{id=$Version;tag_name=('v'+$Version);draft=$false;prerelease=$false;immutable=$false;
        updated_at='2026-11-01T00:00:00Z';assets=@(
            [pscustomobject]@{id=($Version+'-exe');name='setup.exe';size=100;digest='sha256:fixture';hash_value='fixture'},
            [pscustomobject]@{id=($Version+'-apk');name='app.apk';size=100;digest='sha256:fixture';hash_value='fixture'},
            [pscustomobject]@{id=($Version+'-dmg');name='app.dmg';size=100;digest='sha256:fixture';hash_value='fixture'}
        )}
}
function Reset-Fixture([int]$Count=5){
    $global:HarnessRetentionFixture=[ordered]@{Releases=@(1..$Count|ForEach-Object {New-Fixture "2.0.$_"});Deleted=@();Latest="2.0.$Count";Stale=$false;FailPage2=$false;Pages=@()}
}
function Invoke-RestMethod {
    param([string]$Uri,[string]$Method='Get',$Headers,[int]$TimeoutSec,[switch]$NoProxy)
    $state=$global:HarnessRetentionFixture
    if($Headers -and $Headers.Authorization -ne 'Bearer fixture'){throw 'Offline test attempted to use a real credential.'}
    if($Uri -match '/-/git/raw/updates/(releases.win|android|macos)\.json$'){
        return [pscustomobject]@{Assets=@([pscustomobject]@{Version='2.0.1'});versionName='2.0.1';version='2.0.1'}
    }
    if($Uri -match '/releases\?page=(\d+)&(page_size|per_page)=100$'){
        $page=[int]$Matches[1];$state.Pages+=@($page)
        if($page -eq 2 -and $state.FailPage2){throw 'Fixture page two is unavailable.'}
        return @($state.Releases|Select-Object -Skip (($page-1)*100) -First 100)
    }
    if($Uri -match '/releases/latest$'){return $state.Releases|Where-Object id -eq $state.Latest}
    if($Uri -match '/releases/(2\.0\.\d+)$'){
        $id=$Matches[1]
        if($Method -eq 'Delete'){
            $state.Deleted+=@($id);$state.Releases=@($state.Releases|Where-Object id -ne $id)
            return
        }
        $release=$state.Releases|Where-Object id -eq $id
        if($state.Stale){$release=$release.PSObject.Copy();$release.updated_at='2026-11-02T00:00:00Z'}
        return $release
    }
    throw "Unexpected fixture request: $Method $Uri"
}
try{
    Reset-Fixture
    & $runner -Provider GitHub -AuditDirectory (Join-Path $auditRoot 'preview')
    if($global:HarnessRetentionFixture.Deleted.Count){throw 'Preview deleted a release.'}
    Reset-Fixture
    & $runner -Provider GitHub -Apply -AuditDirectory (Join-Path $auditRoot 'apply')
    if(($global:HarnessRetentionFixture.Deleted -join ',') -ne '2.0.1,2.0.2'){throw 'Apply did not remove exactly the oldest two versions.'}
    Reset-Fixture
    & $runner -Provider CNB -Apply -AuditDirectory (Join-Path $auditRoot 'feeds')
    if(($global:HarnessRetentionFixture.Deleted -join ',') -ne '2.0.2'){throw 'An actively referenced CNB version was not protected.'}
    Reset-Fixture 101
    & $runner -Provider GitHub -AuditDirectory (Join-Path $auditRoot 'pagination')
    if(($global:HarnessRetentionFixture.Pages -join ',') -ne '1,2'){throw 'Pagination did not read the complete release history.'}
    Reset-Fixture 101
    $global:HarnessRetentionFixture.FailPage2=$true;$rejected=$false
    try{& $runner -Provider GitHub -Apply -AuditDirectory (Join-Path $auditRoot 'partial')}catch{$rejected=$true}
    if(!$rejected -or $global:HarnessRetentionFixture.Deleted.Count){throw 'A partial inventory must not delete releases.'}
    Reset-Fixture
    $global:HarnessRetentionFixture.Stale=$true;$rejected=$false
    try{& $runner -Provider GitHub -Apply -AuditDirectory (Join-Path $auditRoot 'stale')}catch{$rejected=$true}
    if(!$rejected -or $global:HarnessRetentionFixture.Deleted.Count){throw 'A changed candidate must not be deleted.'}
    Write-Output 'Release retention API runner: 6 offline scenarios passed.'
}finally{
    $env:GITHUB_TOKEN=$previousGitHub;$env:CNB_TOKEN=$previousCnb;$env:CNB_TOKEN_FILE=$previousTokenFile
    Remove-Variable HarnessRetentionFixture -Scope Global -ErrorAction SilentlyContinue
}
