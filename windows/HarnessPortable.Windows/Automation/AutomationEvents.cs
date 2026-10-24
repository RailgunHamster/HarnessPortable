using System.Text.Json.Nodes;

namespace HarnessPortable.Windows.Automation;

/// <summary>Bounded, cursor-based observations. Event payloads deliberately exclude operation arguments.</summary>
public sealed class AutomationEvents
{
    private readonly object _gate=new();
    private readonly Queue<JsonObject> _events=[];
    private long _sequence;
    private TaskCompletionSource _changed=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Publish(string kind,JsonObject? data=null)
    {
        TaskCompletionSource notify;
        lock(_gate)
        {
            _events.Enqueue(new(){["sequence"]=++_sequence,["kind"]=kind,["utc"]=DateTimeOffset.UtcNow.ToString("O"),["data"]=data?.DeepClone()});
            while(_events.Count>1024)_events.Dequeue();
            notify=_changed;_changed=new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        notify.TrySetResult();
    }
    public JsonObject Read(long cursor,int limit=100)
    {
        if(cursor<0||limit is <1 or >1024)throw new AutomationFault("invalid_arguments","Invalid event cursor or limit.");
        lock(_gate)
        {
            if(cursor>_sequence)throw new AutomationFault("invalid_arguments","Cursor belongs to a different or older API session.");
            var oldest=_events.Count==0 ? _sequence+1 : _events.Peek()["sequence"]!.GetValue<long>();
            var batch=_events.Where(e=>e["sequence"]!.GetValue<long>()>cursor).Take(limit).ToArray();
            return new(){["cursor"]=batch.Length==0 ? cursor : batch[^1]["sequence"]!.GetValue<long>(),["latestCursor"]=_sequence,
                ["truncated"]=cursor<oldest-1,["events"]=new JsonArray(batch.Select(e=>e.DeepClone()).ToArray())};
        }
    }
    public async Task<JsonNode?> WaitAsync(JsonObject arguments,CancellationToken cancellation)
    {
        var cursor=arguments.Long("cursor");
        var timeout=arguments.Integer("timeoutMs",5000);
        if(timeout is <1 or >30000)throw new AutomationFault("invalid_arguments","timeoutMs must be between 1 and 30000.");
        Task wait;
        lock(_gate)
        {
            if(cursor<_sequence)return Read(cursor);
            Read(cursor);wait=_changed.Task;
        }
        try{await wait.WaitAsync(TimeSpan.FromMilliseconds(timeout),cancellation).ConfigureAwait(false);}
        catch(TimeoutException){var result=Read(cursor);result["timedOut"]=true;return result;}
        return Read(cursor);
    }
    public void Register(AutomationApi api)
    {
        var types=new Dictionary<string,string>{{"cursor","integer"},{"limit","integer"}};
        api.Register(new("events.read","Read observed operations/domain changes after a cursor; gaps are explicitly reported.",["cursor","limit"],false,["cursor","limit"],types),
            (args,_)=>Task.FromResult<JsonNode?>(Read(args.Long("cursor"),args.Integer("limit",100))));
        api.Register(new("events.wait","Wait for observable changes without polling or blocking the UI.",["cursor","timeoutMs"],false,["cursor","timeoutMs"],new Dictionary<string,string>{{"cursor","integer"}}),WaitAsync);
    }
}
