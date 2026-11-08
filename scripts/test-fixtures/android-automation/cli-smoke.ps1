[CmdletBinding()]
param([Parameter(Mandatory)][string]$Serial,[string]$AdbPath='adb',[string]$Package='com.harness.portable.syncqa')
$ErrorActionPreference='Stop'
if($Serial -notmatch '^emulator-\d+$' -or !$Package.EndsWith('.syncqa')){throw 'Only a disposable emulator and isolated QA package are allowed.'}
$scripts=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
& $AdbPath -s $Serial shell pm clear $Package | Out-Null
if($LASTEXITCODE -ne 0){throw 'Cannot clear the QA package.'}
$session=& (Join-Path $scripts 'Start-HarnessAndroidApi.ps1') -Serial $Serial -Package $Package -AdbPath $AdbPath
$endpoint=$session.EndpointFile
try{
    $catalog=& (Join-Path $scripts 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpoint -Method api.describe
    if($catalog.methods.name -notcontains 'ui.act'){throw 'Missing native UI methods.'}
    & (Join-Path $scripts 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpoint -Method app.show -Wait | Out-Null
    $answer=& (Join-Path $scripts 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpoint -Method network.resolve -Parameters @{host='127.0.0.1'} -Wait
    if($answer.address -ne '127.0.0.1'){throw 'The native resolver did not return the loopback fixture.'}
    $pending=& (Join-Path $scripts 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpoint -Method network.resolve -Parameters @{host='127.0.0.1'}
    $job=& (Join-Path $scripts 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpoint -Method operations.get -Parameters @{operationId=$pending.operationId} -Wait
    if(!$job.state -or $job.operationId -ne $pending.operationId){throw 'A status query was incorrectly treated as a new operation.'}
    & (Join-Path $scripts 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpoint -Method app.settings -Wait | Out-Null
    & (Join-Path $scripts 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpoint -Method settings.tab -Arguments '{"value":"sync"}' -Wait | Out-Null
    $controls=@(& (Join-Path $scripts 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpoint -Method ui.find -Parameters @{selector=@{automationId='ConfigurationSync.Address'}})
    if($controls.Count -ne 1){throw 'Settings tab UI is not reachable through the PowerShell client.'}
    & (Join-Path $scripts 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpoint -Method ui.act -Parameters @{target=@{automationId='ConfigurationSync.Address'};action='setValue';value='https://dav.example.test/fixture/'} -Wait | Out-Null
    & (Join-Path $scripts 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpoint -Method app.back -Wait | Out-Null
    $exitAck=& (Join-Path $scripts 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpoint -Method app.exit
    if(!$exitAck.operationId){throw 'Exit was not acknowledged before closing the application.'}
    $closed=$false; $watch=[Diagnostics.Stopwatch]::StartNew()
    while($watch.Elapsed.TotalSeconds -lt 8){
        try{& (Join-Path $scripts 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpoint -Method api.describe -TimeoutSeconds 1 | Out-Null}
        catch{$closed=$true;break}
        Start-Sleep -Milliseconds 80
    }
    if(!$closed){throw 'Application exit did not close the native API session.'}
}finally{& (Join-Path $scripts 'Stop-HarnessAndroidApi.ps1') -EndpointFile $endpoint | Out-Null}
if(Test-Path -LiteralPath $endpoint){throw 'Endpoint file was not removed.'}
'PASS: PowerShell start, endpoint, JSON/object parameters, native UI mutation, task queries, acknowledged exit, and stop cleanup.'
