using System.Text.Json.Nodes;
using HarnessPortable.Windows.Automation;

namespace HarnessPortable.Windows.Tests;

public sealed class AutomationApiTests
{
    private static JsonObject Request(string method, JsonObject? arguments = null) => new()
        { ["version"]=1, ["id"]="test", ["method"]=method, ["params"]=arguments ?? new() };

    [Fact]
    public async Task DiscoveryDescribesRegisteredParametersAndMutations()
    {
        var api=new AutomationApi();
        api.Register(new("fixture.echo","Echo a value.",["value"],false),(args,_)=>Task.FromResult(args["value"]?.DeepClone()));
        var result=await api.CallAsync(Request("api.describe"));
        var methods=result["result"]!["methods"]!.AsArray();
        Assert.Contains(methods,n=>n!["name"]!.GetValue<string>()=="fixture.echo" && n["parameters"]![0]!.GetValue<string>()=="value");
        Assert.Equal("test",result["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task UnknownArgumentsNeverExecuteTheHandler()
    {
        var called=false; var api=new AutomationApi();
        api.Register(new("fixture.write","Mutation.",["value"],true),(_,_)=>{called=true;return Task.FromResult<JsonNode?>(null);});
        var reply=await api.CallAsync(Request("fixture.write",new(){["wrong"]=true}));
        Assert.Equal("invalid_arguments",reply["error"]!["code"]!.GetValue<string>()); Assert.False(called);
        var unknown=await api.CallAsync(Request("does.not.exist"));
        Assert.Equal("unknown_method",unknown["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task UnexpectedErrorsDoNotEchoCredentialsOrExceptionMessages()
    {
        var api=new AutomationApi();
        api.Register(new("fixture.fail","Failure.",[],true),(_,_)=>throw new IOException("password=never-echo-this"));
        var reply=await api.CallAsync(Request("fixture.fail"));
        Assert.Equal("internal_error",reply["error"]!["code"]!.GetValue<string>());
        Assert.DoesNotContain("never-echo-this",reply.ToJsonString());
    }

    [Fact]
    public async Task CancellationAndInvalidProtocolAreStructuredFailures()
    {
        var api=new AutomationApi(); using var cancellation=new CancellationTokenSource(); cancellation.Cancel();
        var reply=await api.CallAsync(Request("api.describe"),cancellation.Token);
        Assert.Equal("cancelled",reply["error"]!["code"]!.GetValue<string>());
        var request=Request("api.describe"); request["version"]=99;
        reply=await api.CallAsync(request);
        Assert.Equal("invalid_request",reply["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void DuplicateMethodsFailInsteadOfReplacingAnotherOperation()
    {
        var api=new AutomationApi();
        Assert.Throws<InvalidOperationException>(()=>api.Register(new("api.describe","Duplicate.",[],false),(_,_)=>Task.FromResult<JsonNode?>(null)));
    }
}
