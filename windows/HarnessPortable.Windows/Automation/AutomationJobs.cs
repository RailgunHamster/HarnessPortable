using System.Text.Json.Nodes;
using System.Windows.Threading;

namespace HarnessPortable.Windows.Automation;

/// <summary>Modal dialogs must not hold the API connection or prevent a second request.</summary>
public sealed class AutomationJobs(Dispatcher dispatcher, AutomationEvents? events=null)
{
    private sealed record Job(string Id, string Method)
    {
        public string State { get; set; } = "queued";
        public JsonNode? Result { get; set; }
        public string? Error { get; set; }
        public string? ErrorType { get; set; }
        public string[]? ErrorSite { get; set; }
        public long ResultBytes { get; set; }
        public Func<Task<JsonNode?>>? Action { get; set; }
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Job> _jobs = [];
    private readonly Queue<string> _finished = [];
    private const int RetainedJobs = 256;
    private const long MaximumResultBytes = 16*1024*1024;
    private const long RetainedResultBytes = 64*1024*1024;
    private long _resultBytes;

    public JsonNode Start(string method, Func<Task<JsonNode?>> action)
    {
        Job job;
        lock (_gate)
        {
            if (_jobs.Values.Count(j => j.State is "queued" or "running") >= 64)
                throw new AutomationFault("busy", "Too many outstanding UI operations.");
            job = new(Guid.NewGuid().ToString("N"), method) { Action = action };
            _jobs.Add(job.Id, job);
            Observe(job);
        }
        return Describe(job.Id);
    }

    internal void ReplyDelivered(JsonObject reply, bool delivered)
    {
        var id = (reply["result"] as JsonObject)?["operationId"]?.GetValue<string>();
        if (id is null) return;
        Job job;
        Func<Task<JsonNode?>> action;
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out job!) || job.Action is null) return;
            action = job.Action; job.Action = null;
            if (!delivered) { job.State = "failed"; job.Error = "reply_not_delivered"; Observe(job); Retain(job.Id); return; }
        }
        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
        {
            lock (_gate) { job.State = "running"; Observe(job); }
            try
            {
                var result = await action();
                var size=result is null ? 0 : System.Text.Encoding.UTF8.GetByteCount(result.ToJsonString());
                if(size>MaximumResultBytes)throw new AutomationFault("result_too_large","Operation result exceeds 16 MiB.");
                lock (_gate) { job.Result = result; job.ResultBytes=size; _resultBytes+=size; job.State = "returned"; }
            }
            catch (Exception error)
            {
                lock (_gate)
                {
                    job.Error = error is AutomationFault fault ? fault.Code : "operation_failed"; job.State = "failed";
                    job.ErrorType=error.GetType().FullName;
                    job.ErrorSite=new System.Diagnostics.StackTrace(error,false).GetFrames().Take(8)
                        .Select(f=>f.GetMethod() is { } m ? m.DeclaringType?.FullName+"."+m.Name : "unknown").ToArray();
                }
            }
            finally
            {
                lock (_gate)
                {
                    Observe(job);
                    Retain(job.Id);
                }
            }
        }));
    }

    private void Observe(Job job)=>events?.Publish("operation."+job.State,new(){["operationId"]=job.Id,["method"]=job.Method,["errorCode"]=job.Error});

    private void Retain(string id)
    {
        _finished.Enqueue(id);
        while (_finished.Count > RetainedJobs || _resultBytes>RetainedResultBytes)
            if(_jobs.Remove(_finished.Dequeue(),out var removed))_resultBytes-=removed.ResultBytes;
    }

    public JsonObject Describe(string id)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(id, out var job)) throw new AutomationFault("not_found", "Operation does not exist or has expired.");
            return new() { ["operationId"] = job.Id, ["method"] = job.Method, ["state"] = job.State,
                ["result"] = job.Result?.DeepClone(), ["errorCode"] = job.Error, ["errorType"]=job.ErrorType,
                ["errorSite"]=job.ErrorSite is null ? null : new JsonArray(job.ErrorSite.Select(s=>(JsonNode?)JsonValue.Create(s)).ToArray()) };
        }
    }
}
