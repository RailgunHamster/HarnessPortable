using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using HarnessPortable.Windows.Automation;

namespace HarnessPortable.Windows.Tests;

public sealed class AutomationPipeTests
{
    private static async Task<JsonObject> Exchange(string name,string line)
    {
        using var cancellation=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);
        await pipe.ConnectAsync(cancellation.Token);
        using var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);
        using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};
        await writer.WriteLineAsync(line.AsMemory(),cancellation.Token);
        return JsonNode.Parse(await reader.ReadLineAsync(cancellation.Token)??throw new IOException("No response."))!.AsObject();
    }

    [Fact]
    public async Task ConcurrentRequestsDoNotWaitForAnUnrelatedLongOperation()
    {
        var directory=Path.Combine(Path.GetTempPath(),"HarnessApiTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var endpoint=Path.Combine(directory,"endpoint.json"); var api=new AutomationApi();
        var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Register(new("fixture.wait","Wait.",[],false),async(_,token)=>{await gate.Task.WaitAsync(token);return JsonValue.Create(true);});
        try
        {
            await using(var server=new AutomationPipeServer(api,endpoint))
            {
                Assert.True(JsonNode.Parse(File.ReadAllText(endpoint))!["currentUserOnly"]!.GetValue<bool>());
                var slow=Exchange(server.PipeName,"{\"version\":1,\"id\":\"slow\",\"method\":\"fixture.wait\"}");
                var discovery=await Exchange(server.PipeName,"{\"version\":1,\"id\":\"fast\",\"method\":\"api.describe\"}");
                Assert.Equal("fast",discovery["id"]!.GetValue<string>()); Assert.False(slow.IsCompleted);
                gate.SetResult(); Assert.True((await slow)["result"]!.GetValue<bool>());
                var malformed=await Exchange(server.PipeName,"not-json"); Assert.Equal("invalid_json",malformed["error"]!["code"]!.GetValue<string>());
            }
            Assert.False(File.Exists(endpoint));
        }
        finally{gate.TrySetResult();if(Directory.Exists(directory))Directory.Delete(directory);}
    }

    [Fact]
    public async Task ExistingDescriptorIsNotOverwrittenAndIdleClientDoesNotBlockShutdown()
    {
        var directory=Path.Combine(Path.GetTempPath(),"HarnessApiTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var endpoint=Path.Combine(directory,"endpoint.json"); var server=new AutomationPipeServer(new(),endpoint);
        try
        {
            var saved=File.ReadAllText(endpoint);
            Assert.Throws<IOException>(()=>new AutomationPipeServer(new(),endpoint)); Assert.Equal(saved,File.ReadAllText(endpoint));
            await using var pipe=new NamedPipeClientStream(".",server.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
            await pipe.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(File.Exists(endpoint));
        }
        finally{server.Stop();if(Directory.Exists(directory))Directory.Delete(directory);}
    }
}
