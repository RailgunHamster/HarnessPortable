[CmdletBinding()]
param([Parameter(Mandatory)][string]$ProductExe,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$scripts=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$instance=& (Join-Path $scripts 'Start-HarnessApi.ps1') -ProductExe $ProductExe -OutputDirectory $OutputDirectory -Offscreen
$child=Get-Process -Id $instance.ProcessId -ErrorAction Stop
$null=$child.Handle
function Invoke-Client([string]$Method,[string]$Arguments='{}',[switch]$Wait) {
    $json=& (Join-Path $scripts 'Invoke-HarnessApi.ps1') -EndpointFile $instance.EndpointFile -Method $Method -Arguments $Arguments -Wait:$Wait
    if ($json -isnot [string]) { throw 'CLI must emit exactly one JSON string, without task or helper output.' }
    return ($json | ConvertFrom-Json -Depth 64).result
}
try {
    $catalog=Invoke-Client 'api.describe'
    if ('events.wait' -notin $catalog.methods.name) { throw 'API methods were not discovered.' }
    $toggle=Invoke-Client 'ui.act' '{"target":{"automationId":"MainWindow.SidebarToggleButton"},"action":"invoke"}' -Wait
    if ($toggle.state -ne 'returned') { throw 'CLI -Wait did not return a completed operation.' }
    $state=Invoke-Client 'app.state'
    if ($state.sidebarVisible) { throw 'CLI did not execute the normal sidebar action.' }
    $exit=Invoke-Client 'app.exit'
    if (!$exit.operationId) { throw 'Exit acknowledgment missing.' }
    if (!$child.WaitForExit(15000)) { throw 'Isolated CLI instance did not exit within 15 seconds.' }
    if ($child.ExitCode -ne 0) { throw "Isolated CLI exit code: $($child.ExitCode)." }
    if (Test-Path -LiteralPath $instance.EndpointFile) { throw 'Isolated CLI endpoint was not removed on exit.' }
    [pscustomobject]@{passed=$true;methods=$catalog.methods.Count;checks=4} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $instance.OutputDirectory 'cli-verification.json')
    Write-Output 'PASS: CLI discovery, UI command, operation waiting and clean exit.'
} finally {
    if (!$child.HasExited) { $child.Kill($true);$child.WaitForExit() }
    $child.Dispose()
}
