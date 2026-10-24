using System.Text.Json.Nodes;

namespace HarnessPortable.Windows.Automation;

public sealed class AutomationFault(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record AutomationMethod(string Name, string Description, string[] Parameters, bool Mutates, string[]? OptionalParameters = null, IReadOnlyDictionary<string,string>? ParameterTypes = null)
{
    public JsonObject InputSchema
    {
        get
        {
            var properties = new JsonObject();
            foreach (var name in Parameters)
                properties[name] = ParameterTypes?.TryGetValue(name,out var declaredType)==true ? new JsonObject{["type"]=declaredType} : name switch
                {
                    "target" or "selector" or "parameters" => new JsonObject { ["type"]="object", ["minProperties"]=name=="parameters" ? 0 : 1 },
                    "includeHidden" or "exists" or "enabled" or "after" or "regroup" or "commit" or "raw" => new JsonObject { ["type"]="boolean" },
                    "timeoutMs" or "index" or "start" or "length" => new JsonObject { ["type"]="integer" },
                    "vertical" or "horizontal" or "widthPixels" or "heightPixels" => new JsonObject { ["type"]="number" },
                    "value" or "equals" => new JsonObject(),
                    _ => new JsonObject { ["type"]="string" }
                };
            return new() { ["type"]="object", ["additionalProperties"]=false, ["properties"]=properties,
                ["required"]=new JsonArray(Parameters.Where(p => OptionalParameters?.Contains(p) != true).Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()) };
        }
    }
}

/// <summary>Public in-process API. Transports and tests use the same dispatcher.</summary>
public sealed class AutomationApi
{
    public const int ProtocolVersion = 1;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (AutomationMethod Info, Func<JsonObject, CancellationToken, Task<JsonNode?>> Run)> _methods = new();

    public AutomationApi()
    {
        Register(new("api.describe", "Discover methods and their accepted parameters.", [], false), (_, _) =>
            Task.FromResult<JsonNode?>(System.Text.Json.JsonSerializer.SerializeToNode(new
            {
                protocol = "harness.automation", version = ProtocolVersion,
                methods = _methods.Values.Select(v => v.Info).OrderBy(m => m.Name)
            }, JsonOptions)));
    }

    public static System.Text.Json.JsonSerializerOptions JsonOptions { get; } = new(System.Text.Json.JsonSerializerDefaults.Web);
    public IReadOnlyCollection<AutomationMethod> Methods => _methods.Values.Select(x => x.Info).ToArray();
    internal event Action<JsonObject, bool>? ReplyDelivery;
    internal void CompleteDelivery(JsonObject reply, bool delivered, string? method)
    {
        if(method is not null && _methods.TryGetValue(method,out var registered) && registered.Info.Mutates && reply["error"] is null)
            ReplyDelivery?.Invoke(reply, delivered);
    }

    public void Register(AutomationMethod method, Func<JsonObject, CancellationToken, Task<JsonNode?>> handler)
    {
        var validTypes=new[]{"string","integer","number","boolean","object","array","null"};
        if(string.IsNullOrWhiteSpace(method.Name)||method.Parameters.Distinct().Count()!=method.Parameters.Length ||
            method.OptionalParameters?.Any(p=>!method.Parameters.Contains(p))==true ||
            method.ParameterTypes?.Any(p=>!method.Parameters.Contains(p.Key)||!validTypes.Contains(p.Value))==true)
            throw new ArgumentException("Invalid API method definition.");
        if (!_methods.TryAdd(method.Name, (method, handler))) throw new InvalidOperationException("Duplicate API method: " + method.Name);
    }

    public async Task<JsonObject> CallAsync(JsonObject request, CancellationToken cancellation = default, bool deferActionsUntilReply = false)
    {
        JsonNode? id = null;
        try
        {
            if(request["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var requestId) || string.IsNullOrEmpty(requestId) || requestId.Length>128)
                throw new AutomationFault("invalid_request","Request id must be a short string.");
            id=JsonValue.Create(requestId);
            if (request.Any(p => p.Key is not ("version" or "id" or "method" or "params")) ||
                request["version"] is not JsonValue protocol || !protocol.TryGetValue<int>(out var version) || version!=ProtocolVersion)
                throw new AutomationFault("invalid_request", "Expected version=1, a short request id, method, and object params.");
            var name = request["method"]?.GetValue<string>() ?? throw new AutomationFault("invalid_request", "Method is required.");
            if (!_methods.TryGetValue(name, out var method)) throw new AutomationFault("unknown_method", "Method is not registered.");
            var arguments = request["params"] is null ? new JsonObject() : request["params"] as JsonObject
                ?? throw new AutomationFault("invalid_arguments", "Parameters must be an object.");
            if (arguments.Any(p => !method.Info.Parameters.Contains(p.Key)))
                throw new AutomationFault("invalid_arguments", "Unknown parameter. Inspect api.describe for the method signature.");
            var inputSchema=method.Info.InputSchema;
            foreach (var parameter in method.Info.Parameters)
            {
                if (!arguments.ContainsKey(parameter))
                {
                    if (method.Info.OptionalParameters?.Contains(parameter) == true) continue;
                    throw new AutomationFault("invalid_arguments", "Missing parameter: " + parameter);
                }
                var expected = inputSchema["properties"]![parameter]?["type"]?.GetValue<string>();
                var kind = arguments[parameter]?.GetValueKind();
                var valid = expected switch
                {
                    "object" => arguments[parameter] is JsonObject,
                    "string" => kind == System.Text.Json.JsonValueKind.String,
                    "boolean" => kind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False,
                    "integer" => kind == System.Text.Json.JsonValueKind.Number && (arguments[parameter]!.AsValue().TryGetValue<int>(out _) || arguments[parameter]!.AsValue().TryGetValue<long>(out _)),
                    "number" => kind == System.Text.Json.JsonValueKind.Number,
                    "array" => arguments[parameter] is JsonArray,
                    "null" => arguments[parameter] is null,
                    _ => true
                };
                if (!valid) throw new AutomationFault("invalid_arguments", "Wrong parameter type: " + parameter);
            }
            cancellation.ThrowIfCancellationRequested();
            var result = await method.Run(arguments, cancellation).ConfigureAwait(false);
            var reply = new JsonObject { ["version"] = ProtocolVersion, ["id"] = id, ["result"] = result };
            if (!deferActionsUntilReply) CompleteDelivery(reply, true, name);
            return reply;
        }
        catch (Exception error)
        {
            var code = error switch
            {
                AutomationFault knownFault => knownFault.Code,
                OperationCanceledException => "cancelled",
                InvalidOperationException or FormatException or System.Text.Json.JsonException => "invalid_arguments",
                _ => "internal_error"
            };
            // Never echo arguments, credentials, URLs, or exception messages from third-party code.
            return new() { ["version"] = ProtocolVersion, ["id"] = id,
                ["error"] = new JsonObject { ["code"] = code,
                    ["message"] = error is AutomationFault fault ? fault.Message : "Request failed: " + code } };
        }
    }
}

internal static class ApiArguments
{
    public static string String(this JsonObject args, string name) => args[name] is JsonValue value && value.TryGetValue<string>(out var result)
        ? result : throw new AutomationFault("invalid_arguments", "Expected string parameter: " + name);
    public static string? OptionalString(this JsonObject args, string name) => args[name] is null ? null : args.String(name);
    public static long Long(this JsonObject args,string name,long fallback=0)
    {
        if(args[name] is null)return fallback;
        if(args[name] is JsonValue value)
        {
            if(value.TryGetValue<long>(out var number))return number;
            if(value.TryGetValue<int>(out var integer))return integer;
        }
        throw new AutomationFault("invalid_arguments","Expected integer parameter: "+name);
    }
    public static int Integer(this JsonObject args,string name,int fallback=0)
    {
        var number=args.Long(name,fallback);
        if(number is < int.MinValue or > int.MaxValue)throw new AutomationFault("invalid_arguments","Integer is out of range: "+name);
        return (int)number;
    }
    public static double Number(this JsonObject args,string name)
    {
        if(args[name] is not JsonValue value || value.GetValueKind()!=System.Text.Json.JsonValueKind.Number)
            throw new AutomationFault("invalid_arguments","Expected numeric parameter: "+name);
        if(value.TryGetValue<double>(out var number))return number;
        if(value.TryGetValue<int>(out var integer))return integer;
        if(value.TryGetValue<long>(out var longNumber))return longNumber;
        return double.Parse(value.ToJsonString(),System.Globalization.CultureInfo.InvariantCulture);
    }
    public static bool Boolean(this JsonObject args, string name, bool fallback = false) => args[name] is null ? fallback :
        args[name] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : throw new AutomationFault("invalid_arguments", "Expected boolean parameter: " + name);
}
