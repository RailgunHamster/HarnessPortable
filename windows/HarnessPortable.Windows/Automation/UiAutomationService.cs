using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using Application = System.Windows.Application;

namespace HarnessPortable.Windows.Automation;

public sealed class UiAutomationService : IDisposable
{
    public AutomationApi Api { get; } = new();
    public AutomationJobs Jobs { get; }
    public WpfAutomationTree Tree { get; }
    public AutomationEvents Events { get; } = new();
    private readonly List<Action> _detach=[];
    public void Track(Action attach,Action detach){attach();_detach.Add(detach);}
    public void Dispose(){foreach(var detach in _detach)detach();_detach.Clear();}
    private readonly Dispatcher _dispatcher;

    public UiAutomationService(Dispatcher dispatcher, Func<IEnumerable<Window>> windows)
    {
        _dispatcher = dispatcher; Jobs = new(dispatcher,Events); Tree = new(windows);
        Api.ReplyDelivery += Jobs.ReplyDelivered;
        Events.Register(Api);
        RegisterQuery("ui.actions", "Discover semantic UI action signatures, including required and optional action arguments.", [], _ => UiActionContract.Describe());
        RegisterQuery("ui.tree", "Discover current controls, state, stable automation ids and available actions.", ["includeHidden"], args => Tree.Snapshot(args.Boolean("includeHidden")), ["includeHidden"]);
        RegisterQuery("ui.find", "Find controls using selector fields; does not silently choose an ambiguous target.", ["selector", "includeHidden"], args => Find(args), ["includeHidden"]);
        RegisterCommand("ui.act", "Dispatch a semantic control action. Wait for its observable UI/domain postcondition.",
            ["target", "action", "value", "index", "start", "length", "vertical", "horizontal", "commit", "mode", "widthPixels", "heightPixels"], args => Task.FromResult(Tree.Act(args)), ["value", "index", "start", "length", "vertical", "horizontal", "commit", "mode", "widthPixels", "heightPixels"]);
        Api.Register(new("operations.get", "Read a dispatched operation. Returned means the handler returned, not that a background connection finished.", ["operationId"], false),
            (args, _) => Task.FromResult<JsonNode?>(Jobs.Describe(args.String("operationId"))));
        Api.Register(new("ui.wait", "Wait for an observable UI condition without blocking the UI dispatcher.",
            ["selector", "includeHidden", "property", "equals", "exists", "timeoutMs"], false, ["includeHidden", "property", "equals", "exists", "timeoutMs"]), WaitAsync);
    }

    public void RegisterQuery(string name, string description, string[] parameters, Func<JsonObject, JsonNode?> handler, string[]? optional = null) =>
        Api.Register(new(name, description, parameters, false, optional), (args, cancellation) => OnUi(() => handler(args), cancellation));

    public void RegisterCommand(string name, string description, string[] parameters, Func<JsonObject, Task<JsonNode?>> handler, string[]? optional = null) =>
        Api.Register(new(name, description, parameters, true, optional), (args, _) =>
        {
            var copy = (JsonObject)args.DeepClone();
            return Task.FromResult<JsonNode?>(Jobs.Start(name, () => handler(copy)));
        });

    public void RegisterCommand(string name, string description, string[] parameters, Action<JsonObject> handler, string[]? optional = null) =>
        RegisterCommand(name, description, parameters, args => { handler(args); return Task.FromResult<JsonNode?>(new JsonObject { ["dispatched"] = true }); }, optional);

    private Task<JsonNode?> OnUi(Func<JsonNode?> action, CancellationToken cancellation) =>
        _dispatcher.InvokeAsync(action, DispatcherPriority.Normal, cancellation).Task;

    private JsonArray Find(JsonObject args)
    {
        var selector = args["selector"] as JsonObject ?? throw new AutomationFault("invalid_arguments", "Selector is required.");
        var allowed = new[] { "id", "automationId", "elementName", "type", "name", "dataKey", "tabId", "windowId" };
        if (selector.Count == 0 || selector.Any(p => !allowed.Contains(p.Key)))
            throw new AutomationFault("invalid_arguments", "Use documented selector fields.");
        var matches = Tree.Snapshot(args.Boolean("includeHidden")).OfType<JsonObject>()
            .Where(node => selector.All(p => JsonNode.DeepEquals(node[p.Key], p.Value)));
        return new(matches.Select(n => n.DeepClone()).ToArray());
    }

    private async Task<JsonNode?> WaitAsync(JsonObject args, CancellationToken cancellation)
    {
        var timeout = args.Integer("timeoutMs", 5000);
        if (timeout is < 1 or > 30000) throw new AutomationFault("invalid_arguments", "timeoutMs must be between 1 and 30000.");
        var property = args.OptionalString("property");
        if (property is not null && (property.Length > 80 || property.Split('.').Any(p => p.Length == 0)))
            throw new AutomationFault("invalid_arguments", "Invalid state property path.");
        var exists = args.Boolean("exists", true);
        if (!exists && property is not null) throw new AutomationFault("invalid_arguments", "An absent target has no state property.");
        if (property is not null && !args.ContainsKey("equals")) throw new AutomationFault("invalid_arguments", "A state property assertion requires equals.");
        var search = new JsonObject { ["selector"] = args["selector"]?.DeepClone(), ["includeHidden"] = args.Boolean("includeHidden") };
        var watch = Stopwatch.StartNew();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var found = (JsonArray)(await OnUi(() => Find(search), cancellation))!;
            if (!exists && found.Count == 0) return new JsonObject { ["matched"] = true };
            if (exists && found.Count > 0)
            {
                if (property is null) return found;
                if (found.Count > 1) throw new AutomationFault("ambiguous", "State assertion must identify a single control.");
                JsonNode? value = found[0];
                foreach (var part in property.Split('.'))
                    value = value is JsonObject objectValue && objectValue.TryGetPropertyValue(part, out var fieldValue)
                        ? fieldValue : throw new AutomationFault("invalid_arguments", "The target does not expose the requested state property.");
                if (JsonNode.DeepEquals(value, args["equals"])) return found[0]!.DeepClone();
            }
            if (watch.ElapsedMilliseconds >= timeout) throw new AutomationFault("timeout", "UI condition did not become true before the deadline.");
            await Task.Delay(40, cancellation).ConfigureAwait(false);
        }
    }
}
