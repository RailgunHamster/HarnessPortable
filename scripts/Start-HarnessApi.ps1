[CmdletBinding()]
param(
    [string]$ProductExe = '',
    [string]$OutputDirectory = '',
    [switch]$Offscreen
)
$ErrorActionPreference='Stop'
$repository=Split-Path -Parent $PSScriptRoot
if (!$ProductExe) { $ProductExe=Join-Path $repository 'windows/HarnessPortable.Windows/bin/Release/net10.0-windows/HarnessPortable.exe' }
$ProductExe=[IO.Path]::GetFullPath($ProductExe)
if (!(Test-Path -LiteralPath $ProductExe -PathType Leaf)) { throw 'Build the Windows product first, or pass -ProductExe.' }
if (!$OutputDirectory) { $OutputDirectory=Join-Path ([IO.Path]::GetTempPath()) ('HarnessPortable.Api.'+[Guid]::NewGuid().ToString('N')) }
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $OutputDirectory) -and @(Get-ChildItem -LiteralPath $OutputDirectory -Force).Count -gt 0) { throw 'Use a new empty output directory.' }
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$data=Join-Path $OutputDirectory 'data'
[void][IO.Directory]::CreateDirectory($data)
$updates=Join-Path $data 'empty-updates'
[void][IO.Directory]::CreateDirectory($updates)
$sshConfig=Join-Path $data 'ssh-config'
[IO.File]::WriteAllText($sshConfig,'')
$settings=@{RestoreLastLayoutOnStartup=$false;StartupReconnectPreference=$false;CloseBehavior='exit';UpdateSourceSelected='cnb';SshConfigPath=$sshConfig} | ConvertTo-Json
[IO.File]::WriteAllText((Join-Path $data 'settings.json'),$settings)
$endpoint=Join-Path $OutputDirectory 'endpoint.json'
$start=[Diagnostics.ProcessStartInfo]::new($ProductExe)
$start.UseShellExecute=$false
$start.CreateNoWindow=$true
$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
$start.Environment['HARNESS_PORTABLE_DATA_DIRECTORY']=$data
$start.ArgumentList.Add('--automation-api')
$start.ArgumentList.Add($endpoint)
if ($Offscreen) { $start.ArgumentList.Add('--automation-offscreen') }
$process=[Diagnostics.Process]::Start($start)
try {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while (!(Test-Path -LiteralPath $endpoint)) {
        if ($process.HasExited) { throw 'The isolated API instance exited before becoming ready; inspect its data/crash.log.' }
        if ($watch.Elapsed.TotalSeconds -gt 20) { throw 'The isolated API instance did not publish an endpoint within 20 seconds.' }
        [void][Threading.Tasks.Task]::Delay(40).GetAwaiter().GetResult()
    }
    & (Join-Path $PSScriptRoot 'Invoke-HarnessApi.ps1') -EndpointFile $endpoint -Method api.describe -TimeoutSeconds 10 | Out-Null
    [pscustomobject]@{EndpointFile=$endpoint;ProcessId=$process.Id;DataDirectory=$data;OutputDirectory=$OutputDirectory}
} catch {
    if (!$process.HasExited) { $process.Kill($true) }
    throw
} finally { $process.Dispose() }
