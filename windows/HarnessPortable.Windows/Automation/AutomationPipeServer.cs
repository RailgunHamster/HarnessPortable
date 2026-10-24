using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;

namespace HarnessPortable.Windows.Automation;

/// <summary>Opt-in IPC for the current OS user. One newline-terminated request per connection.</summary>
public sealed class AutomationPipeServer : IAsyncDisposable
{
    public const int MaximumRequestCharacters = 262144;
    private readonly AutomationApi _api;
    private readonly CancellationTokenSource _stop = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, Task> _clients = new();
    private readonly SemaphoreSlim _capacity = new(16);
    private readonly Task _listener;
    private readonly string _descriptor;
    private int _clientId;
    private int _stopped;
    public string PipeName { get; }

    public AutomationPipeServer(AutomationApi api, string descriptorFile)
    {
        if (!Path.IsPathFullyQualified(descriptorFile)) throw new ArgumentException("Endpoint descriptor must have an absolute path.");
        _api = api; _descriptor = Path.GetFullPath(descriptorFile);
        PipeName = "HarnessPortable.Api." + Environment.ProcessId + "." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.GetDirectoryName(_descriptor)!);
        // Refuse to overwrite another instance's discovery file.
        var temporary=_descriptor+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                System.Text.Json.JsonSerializer.Serialize(output,new{protocol="harness.automation",version=1,
                    processId=Environment.ProcessId,pipeName=PipeName,transport="windows.named-pipe",currentUserOnly=true},AutomationApi.JsonOptions);
            File.Move(temporary,_descriptor,overwrite:false);
        }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
        _listener = Task.Run(ListenAsync);
    }

    private async Task ListenAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                await _capacity.WaitAsync(_stop.Token);
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = new(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(_stop.Token);
                    var id = Interlocked.Increment(ref _clientId);
                    var client = ServeAsync(pipe); pipe = null;
                    _clients[id] = client;
                    _ = client.ContinueWith(_ => { _clients.TryRemove(id, out var ignored); }, TaskScheduler.Default);
                }
                catch { pipe?.Dispose(); _capacity.Release(); throw; }
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe)
    {
        JsonObject? reply = null;
        string? method = null;
        var delivered = false;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await using (pipe)
            {
                using var reader = new StreamReader(pipe, new UTF8Encoding(false, true), false, 4096, leaveOpen:true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen:true) { NewLine="\n" };
                var line = await ReadFrameAsync(reader, deadline.Token);
                JsonObject? request;
                try { request = JsonNode.Parse(line, documentOptions: new() { MaxDepth=32 }) as JsonObject; }
                catch (System.Text.Json.JsonException) { request = null; }
                if(request?["method"] is JsonValue methodValue && methodValue.TryGetValue<string>(out var methodName))method=methodName;
                reply = request is null ? new JsonObject { ["version"]=1, ["id"]=null,
                    ["error"]=new JsonObject { ["code"]="invalid_json", ["message"]="Expected a JSON request object." } }
                    : await _api.CallAsync(request, deadline.Token, deferActionsUntilReply:true);
                await writer.WriteLineAsync(reply.ToJsonString().AsMemory(), deadline.Token);
                await writer.FlushAsync(deadline.Token);
                delivered = true;
            }
        }
        catch (Exception error) when (error is IOException or OperationCanceledException or DecoderFallbackException or AutomationFault)
        { /* Disconnected/oversized callers cannot take down the app or leave queued mutations running. */ }
        finally
        {
            if (reply is not null) _api.CompleteDelivery(reply, delivered, method);
            _capacity.Release();
        }
    }

    private static async Task<string> ReadFrameAsync(StreamReader reader, CancellationToken cancellation)
    {
        var result = new StringBuilder(); var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellation);
            if (count == 0) throw new IOException("Request ended before newline.");
            var newline = Array.IndexOf(buffer, '\n', 0, count);
            var used = newline < 0 ? count : newline;
            if (result.Length + used > MaximumRequestCharacters) throw new AutomationFault("request_too_large", "Request exceeds the IPC limit.");
            result.Append(buffer, 0, used);
            if (newline >= 0) return result.ToString();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Stop();
        try { await _listener; await Task.WhenAll(_clients.Values); }
        catch (IOException) { }
        _stop.Dispose(); _capacity.Dispose();
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0) return;
        _stop.Cancel();
        try
        {
            var saved = JsonNode.Parse(File.ReadAllText(_descriptor));
            if (saved?["pipeName"]?.GetValue<string>() == PipeName) File.Delete(_descriptor);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
    }
}
