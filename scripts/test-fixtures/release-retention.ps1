$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '../ReleaseRetention.ps1')
function Release($Version,$Assets=@('installer.exe','installer.apk','installer.dmg'),$Draft=$false,$Preview=$false){
    [pscustomobject]@{id=$Version;tag_name=('v'+$Version);draft=$Draft;prerelease=$Preview;assets=@($Assets|ForEach-Object {[pscustomobject]@{name=$_}})}
}
function Expect-Tags($Actual,[string]$Expected){
    $text=($Actual|ForEach-Object tag_name)-join ','
    if($text -cne $Expected){throw "Expected [$Expected], got [$text]"}
}
$releases=@((Release '2.8.0'),(Release '2.10.0'),(Release '2.7.12'),(Release '2.9.0'),(Release '2.6.0'))
$plan=Get-ReleaseRetentionPlan $releases
Expect-Tags $plan.Keep 'v2.10.0,v2.9.0,v2.8.0'
Expect-Tags $plan.Remove 'v2.6.0,v2.7.12'
$draft=Release '3.0.0' -Draft $true
$preview=Release '2.11.0' -Preview $true
$unknown=Release 'nightly'
$plan=Get-ReleaseRetentionPlan ($releases+@($draft,$preview,$unknown))
Expect-Tags $plan.Keep 'v2.10.0,v2.9.0,v2.8.0'
if($plan.Untouched.Count -ne 3){throw 'Draft, prerelease, and unknown tags must remain untouched.'}
$plan=Get-ReleaseRetentionPlan $releases -ProtectedTags 'v2.6.0'
Expect-Tags $plan.Keep 'v2.10.0,v2.9.0,v2.8.0,v2.6.0'
$platforms=@((Release '2.10.0' @('installer.exe')),(Release '2.9.0' @('installer.exe')),
    (Release '2.8.0' @('installer.exe')),(Release '2.7.0' @('installer.apk')),(Release '2.6.0' @('installer.dmg')),
    (Release '2.5.0' @('installer.apk','installer.dmg')))
$plan=Get-ReleaseRetentionPlan $platforms
Expect-Tags $plan.Keep 'v2.10.0,v2.9.0,v2.8.0,v2.7.0,v2.6.0'
Expect-Tags $plan.Remove 'v2.5.0'
$plan=Get-ReleaseRetentionPlan @((Release '2.8.1'))
if($plan.Remove.Count){throw 'Fewer than three releases must not be removed.'}
$plan=Get-ReleaseRetentionPlan @()
if($plan.Remove.Count -or $plan.Keep.Count){throw 'An empty list must not produce candidates.'}
$plan=Get-ReleaseRetentionPlan $releases -Keep 1
Expect-Tags $plan.Keep 'v2.10.0'
$rejected=$false
try{Get-ReleaseRetentionPlan @((Release '2.8.1'),(Release '2.8.1'))|Out-Null}catch{$rejected=$true}
if(!$rejected){throw 'Duplicate versions must be rejected.'}
Write-Output 'Release retention: 8 offline behavior checks passed.'
