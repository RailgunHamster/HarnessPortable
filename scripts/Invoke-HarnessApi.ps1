# Call an opt-in Harness Portable instance. No coordinates, process injection, or private APIs.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EndpointFile,
    [Parameter(Mandatory)][string]$Method,
    [string]$Arguments = '{}',
    [ValidateRange(1,300)][int]$TimeoutSeconds = 30,
    [switch]$Wait,
    [switch]$NoThrow
)
$ErrorActionPreference='Stop'
$endpoint=Get-Content -LiteralPath $EndpointFile -Raw | ConvertFrom-Json
if ($endpoint.protocol -ne 'harness.automation' -or $endpoint.version -ne 1 -or $endpoint.transport -ne 'windows.named-pipe' -or
    $endpoint.pipeName -notmatch '^HarnessPortable\.Api\.\d+\.[a-f0-9]{32}$') { throw 'Unsupported or invalid endpoint descriptor.' }
$parameters=ConvertFrom-Json -InputObject $Arguments -AsHashtable
if ($parameters -isnot [System.Collections.IDictionary]) { throw 'Arguments must be a JSON object.' }
$deadline=[Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds($TimeoutSeconds))
function Invoke-HarnessCall([string]$callMethod,$callParameters) {
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.', $endpoint.pipeName, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        [void]$pipe.ConnectAsync($deadline.Token).GetAwaiter().GetResult()
        $writer=[IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),4096,$true)
        $reader=[IO.StreamReader]::new($pipe,[Text.UTF8Encoding]::new($false),$false,4096,$true)
        try {
            $request=@{version=1;id=[Guid]::NewGuid().ToString('N');method=$callMethod;params=$callParameters} | ConvertTo-Json -Depth 32 -Compress
            [void]$writer.WriteLineAsync([System.MemoryExtensions]::AsMemory([string]$request),$deadline.Token).GetAwaiter().GetResult()
            [void]$writer.FlushAsync($deadline.Token).GetAwaiter().GetResult()
            $line=$reader.ReadLineAsync($deadline.Token).GetAwaiter().GetResult()
            if (!$line) { throw 'API connection ended without a response.' }
            return ($line | ConvertFrom-Json -Depth 64)
        } finally { $writer.Dispose(); $reader.Dispose() }
    } finally { $pipe.Dispose() }
}
try {
    $reply=Invoke-HarnessCall $Method $parameters
    if ($Wait -and $reply.result.operationId) {
        $operationId=$reply.result.operationId
        do {
            $deadline.Token.ThrowIfCancellationRequested()
            $reply=Invoke-HarnessCall 'operations.get' @{operationId=$operationId}
            if ($reply.error -or $reply.result.state -in @('returned','failed')) { break }
            [void][Threading.Tasks.Task]::Delay(30,$deadline.Token).GetAwaiter().GetResult()
        } while ($true)
    }
    if (!$NoThrow -and $reply.error) { throw ('Harness API: '+$reply.error.code+' — '+$reply.error.message) }
    if (!$NoThrow -and $reply.result.state -eq 'failed') { throw ('Harness operation: '+$reply.result.errorCode) }
    $reply | ConvertTo-Json -Depth 64
} finally { $deadline.Dispose() }
