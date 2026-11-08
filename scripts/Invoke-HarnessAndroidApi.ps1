[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EndpointFile,
    [Parameter(Mandatory)][string]$Method,
    [string]$Arguments='{}',
    [object]$Parameters,
    [switch]$Wait,
    [ValidateRange(1,300)][int]$TimeoutSeconds=30
)
$ErrorActionPreference='Stop'
$endpoint=Get-Content -LiteralPath $EndpointFile -Raw | ConvertFrom-Json
if($endpoint.protocol -ne 1 -or $endpoint.platform -ne 'android' -or $endpoint.host -ne '127.0.0.1' -or $endpoint.port -notin 1..65535 -or $endpoint.token -notmatch '^[a-fA-F0-9]{64}$'){throw 'Invalid Android automation endpoint.'}
$parametersValue=if($PSBoundParameters.ContainsKey('Parameters')){$Parameters}else{ConvertFrom-Json -InputObject $Arguments -AsHashtable}
if($null -eq $parametersValue){$parametersValue=@{}}
$watch=[Diagnostics.Stopwatch]::StartNew()
function Invoke-AndroidCall([string]$CallMethod,$CallParameters){
    $remaining=[Math]::Max(1,$TimeoutSeconds-$watch.Elapsed.TotalSeconds)
    $client=[Net.Sockets.TcpClient]::new()
    try{
        [void]$client.ConnectAsync('127.0.0.1',[int]$endpoint.port).WaitAsync([TimeSpan]::FromSeconds($remaining)).GetAwaiter().GetResult()
        $stream=$client.GetStream();$stream.ReadTimeout=[int]($remaining*1000);$stream.WriteTimeout=[int]($remaining*1000)
        $encoding=[Text.UTF8Encoding]::new($false)
        $writer=[IO.StreamWriter]::new($stream,$encoding,4096,$true);$writer.NewLine="`n";$writer.AutoFlush=$true
        $reader=[IO.StreamReader]::new($stream,$encoding,$false,4096,$true)
        try{
            $id=[Guid]::NewGuid().ToString('N')
            $request=@{jsonrpc='2.0';id=$id;auth=$endpoint.token;method=$CallMethod;params=$CallParameters}|ConvertTo-Json -Depth 40 -Compress
            $writer.WriteLine($request)
            $line=$reader.ReadLineAsync().WaitAsync([TimeSpan]::FromSeconds($remaining)).GetAwaiter().GetResult()
            if(!$line -or $line.Length -gt 2MB){throw 'Invalid automation response.'}
            $reply=ConvertFrom-Json -InputObject $line
            if($reply.id -ne $id){throw 'Automation response identity mismatch.'}
            if($reply.error){throw ('Android API '+$reply.error.code+': '+$reply.error.message)}
            return $reply.result
        }finally{$reader.Dispose();$writer.Dispose()}
    }finally{$client.Dispose()}
}
$result=Invoke-AndroidCall $Method $parametersValue
if($Wait -and $result.operationId -and !$result.state){
    $operationId=$result.operationId
    while($watch.Elapsed.TotalSeconds -lt $TimeoutSeconds){
        $operation=Invoke-AndroidCall 'operations.get' @{operationId=$operationId}
        if($operation.state -eq 'returned'){return $operation.result}
        if($operation.state -eq 'failed'){throw ('Android operation '+$operation.error.code+': '+$operation.error.message)}
        [Threading.Thread]::Sleep(40)
    }
    throw 'Android automation operation timed out; query operations.get or the application status.'
}
$result
