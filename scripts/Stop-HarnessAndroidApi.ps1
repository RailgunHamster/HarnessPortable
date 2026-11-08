[CmdletBinding()]
param([Parameter(Mandatory)][string]$EndpointFile)
$ErrorActionPreference='Stop'
$endpoint=Get-Content -LiteralPath $EndpointFile -Raw|ConvertFrom-Json
if($endpoint.platform -ne 'android' -or $endpoint.packageName -notmatch '^[A-Za-z][A-Za-z0-9_.]+$' -or $endpoint.port -notin 1..65535){throw 'Invalid Android endpoint.'}
& $endpoint.adb -s $endpoint.serial shell am broadcast --receiver-foreground -n ($endpoint.packageName+'/com.harness.portable.automation.AutomationReceiver') --es command stop --es sessionId $endpoint.sessionId | Out-Null
if($LASTEXITCODE -ne 0){throw 'Cannot stop the Android automation session.'}
& $endpoint.adb -s $endpoint.serial forward --remove ('tcp:'+$endpoint.port) | Out-Null
Remove-Item -LiteralPath $EndpointFile -Force
'Android automation stopped; saved application settings are retained.'
