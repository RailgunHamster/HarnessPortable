[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Serial,
    [string]$Package='com.harness.portable',
    [string]$AdbPath='',
    [string]$OutputDirectory=''
)
$ErrorActionPreference='Stop'
if($Package -notmatch '^[A-Za-z][A-Za-z0-9_.]+$'){throw 'Invalid Android package name.'}
if(!$AdbPath){
    $command=Get-Command adb -ErrorAction SilentlyContinue
    if($command){$AdbPath=$command.Source}
    elseif($env:ANDROID_HOME){$AdbPath=Join-Path $env:ANDROID_HOME ('platform-tools/adb'+$(if($IsWindows){'.exe'}else{''}))}
    elseif($IsWindows){$AdbPath=Join-Path $env:LOCALAPPDATA 'Android/Sdk/platform-tools/adb.exe'}
}
if($AdbPath -and !(Test-Path -LiteralPath $AdbPath)){
    $resolvedAdb=Get-Command $AdbPath -CommandType Application -ErrorAction SilentlyContinue
    if($resolvedAdb){$AdbPath=$resolvedAdb.Source}
}
if(!$AdbPath -or !(Test-Path -LiteralPath $AdbPath)){throw 'Android platform-tools adb is unavailable.'}
if(!$OutputDirectory){$OutputDirectory=Join-Path ([IO.Path]::GetTempPath()) ('HarnessPortable.AndroidApi.'+[Guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count){throw 'Use a new empty endpoint directory.'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$endpointPath=Join-Path $OutputDirectory 'endpoint.json'
$token=[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLowerInvariant()
$sessionId=[Guid]::NewGuid().ToString('N')
$hostPort=$null;$started=$false
try{
    $state=(& $AdbPath -s $Serial get-state 2>$null) -join "`n"
    if($LASTEXITCODE -ne 0 -or $state.Trim() -ne 'device'){throw 'The selected ADB device is not ready.'}
    & $AdbPath -s $Serial shell am start --activity-single-top -n ($Package+'/com.harness.portable.MainActivity') | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Cannot open Harness Portable on the selected device.'}
    Start-Sleep -Milliseconds 250
    $reply=(& $AdbPath -s $Serial shell am broadcast --receiver-foreground -n ($Package+'/com.harness.portable.automation.AutomationReceiver') --es command start --es token $token --es sessionId $sessionId --ei port 0) -join "`n"
    if($LASTEXITCODE -ne 0 -or $reply -notmatch 'result=0, data="(\{[^\r\n]+\})"'){throw 'Android automation did not start. Open the upgraded app and check that ADB shell has the DUMP permission.'}
    $descriptor=ConvertFrom-Json -InputObject $Matches[1];$started=$true
    if($descriptor.protocol -ne 1 -or $descriptor.sessionId -ne $sessionId -or $descriptor.port -notin 1..65535){throw 'Unexpected automation session descriptor.'}
    $forward=(& $AdbPath -s $Serial forward tcp:0 ('tcp:'+$descriptor.port)) -join ''
    if($LASTEXITCODE -ne 0 -or $forward.Trim() -notmatch '^\d+$'){throw 'Cannot create the ADB loopback forward.'}
    $hostPort=[int]$forward.Trim()
    $endpoint=[ordered]@{protocol=1;platform='android';host='127.0.0.1';port=$hostPort;devicePort=$descriptor.port;serial=$Serial;packageName=$Package;processId=$descriptor.processId;sessionId=$sessionId;token=$token;adb=$AdbPath}
    [IO.File]::WriteAllText($endpointPath,($endpoint|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    if($IsWindows){
        $acl=[Security.AccessControl.FileSecurity]::new()
        $identity=[Security.Principal.WindowsIdentity]::GetCurrent().User
        $acl.SetOwner($identity);$acl.SetAccessRuleProtection($true,$false)
        $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($identity,[Security.AccessControl.FileSystemRights]::FullControl,[Security.AccessControl.AccessControlType]::Allow))
        [IO.FileSystemAclExtensions]::SetAccessControl([IO.FileInfo]::new($endpointPath),$acl)
    }else{[IO.File]::SetUnixFileMode($endpointPath,[IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite)}
    $watch=[Diagnostics.Stopwatch]::StartNew()
    do{
        $catalog=& (Join-Path $PSScriptRoot 'Invoke-HarnessAndroidApi.ps1') -EndpointFile $endpointPath -Method api.describe -TimeoutSeconds 10
        if($catalog.methods.name -contains 'app.state'){break}
        Start-Sleep -Milliseconds 80
    }while($watch.Elapsed.TotalSeconds -lt 15)
    if($catalog.methods.name -notcontains 'app.state'){throw 'The application UI did not become ready.'}
    [pscustomobject]@{EndpointFile=$endpointPath;Serial=$Serial;Package=$Package;Port=$hostPort;ProcessId=$descriptor.processId}
}catch{
    if($hostPort){& $AdbPath -s $Serial forward --remove ('tcp:'+$hostPort) 2>$null|Out-Null}
    if($started){& $AdbPath -s $Serial shell am broadcast -n ($Package+'/com.harness.portable.automation.AutomationReceiver') --es command stop --es sessionId $sessionId 2>$null|Out-Null}
    if(Test-Path -LiteralPath $endpointPath){Remove-Item -LiteralPath $endpointPath -Force}
    throw
}
