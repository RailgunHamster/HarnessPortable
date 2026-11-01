using System.Diagnostics;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if(args.Length!=2)throw new ArgumentException("Usage: Smoke <product-exe> <new-empty-output-directory>");
var executable=Path.GetFullPath(args[0]); var output=Path.GetFullPath(args[1]);
if(Directory.Exists(output)&&Directory.EnumerateFileSystemEntries(output).Any())throw new InvalidOperationException("Use a new empty fixture directory.");
Directory.CreateDirectory(output); var data=Path.Combine(output,"data"); Directory.CreateDirectory(data);
var sshConfig=Path.Combine(data,"ssh-config");File.WriteAllText(sshConfig,"");
File.WriteAllText(Path.Combine(data,"settings.json"),JsonSerializer.Serialize(new{RestoreLastLayoutOnStartup=false,StartupReconnectPreference=false,SshConfigPath=sshConfig,CloseBehavior="exit",UpdateSourceSelected="cnb"}));
Directory.CreateDirectory(Path.Combine(data,"empty-updates"));
var endpoint=Path.Combine(output,"endpoint.json");
var listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
var port=((IPEndPoint)listener.LocalEndpoint).Port;
using var lifetime=new CancellationTokenSource();
var http=ServeHttp(listener,lifetime.Token);
var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true};
start.ArgumentList.Add("--automation-api");start.ArgumentList.Add(endpoint);start.ArgumentList.Add("--automation-offscreen");
start.Environment["HARNESS_PORTABLE_DATA_DIRECTORY"]=data;
using var product=Process.Start(start)??throw new InvalidOperationException("Product did not start.");
string pipeName="";
var checks=new List<string>();
try
{
    await Until(()=>Task.FromResult(File.Exists(endpoint)),"endpoint ready",20);
    pipeName=JsonNode.Parse(File.ReadAllText(endpoint))!["pipeName"]!.GetValue<string>();
    var catalog=await Call("api.describe");
    File.WriteAllText(Path.Combine(output,"api-catalog.json"),catalog.ToJsonString(new(){WriteIndented=true}));
    var names=catalog["methods"]!.AsArray().Select(m=>m!["name"]!.GetValue<string>()).ToHashSet();
    Check(names.Contains("workspace.tab.move")&&names.Contains("tray.exit")&&names.Contains("ui.wait"),"method discovery includes custom gestures and tray actions");
    await Find("MainWindow.SidebarToggleButton");
    await Act("MainWindow.SidebarToggleButton","invoke");
    await Until(async()=>!(await Call("app.state"))["sidebarVisible"]!.GetValue<bool>(),"sidebar collapse");
    await Act("MainWindow.SidebarToggleButton","invoke");
    await Until(async()=>(await Call("app.state"))["sidebarVisible"]!.GetValue<bool>(),"sidebar expand");
    Check(true,"button API executes normal UI actions");

    var tunnelEditor=await Call("library.create",new(){["kind"]="tunnel"});
    await Find("TunnelEditorWindow.HostBox");
    await Act("TunnelEditorWindow.HostBox","setValue",new(){["value"]="127.0.0.1"});
    await Act("TunnelEditorWindow.UserBox","setValue",new(){["value"]="fixture"});
    await Act("TunnelEditorWindow.PasswordInput","setValue",new(){["value"]="fixture-password"});
    await Act("TunnelEditorWindow.AuthModeBox","select",new(){["index"]=2});
    await Act("TunnelEditorWindow.AuthInputBox","setValue",new(){["value"]="fixture-auth-secret"});
    var snapshot=await Call("ui.tree");
    Check(!snapshot.ToJsonString().Contains("fixture-password")&&!snapshot.ToJsonString().Contains("fixture-auth-secret"),"SSH editor password and authentication inputs never appear in snapshots");
    await Act("TunnelEditorWindow.SshPortBox","setValue",new(){["value"]="70000"});
    var invalidSave=await Call("ui.act",new(){["target"]=new JsonObject{["automationId"]="TunnelEditorWindow.SaveButton"},["action"]="invoke"});
    await Find("dialog.choice.ok");await Act("dialog.choice.ok","invoke");await Job(invalidSave);
    await Find("TunnelEditorWindow.HostBox");
    await Act("TunnelEditorWindow.Button","invoke");await Job(tunnelEditor);
    Check((await Call("library.list")).AsArray().Count==0,"SSH validation follows the real dialog path and cancellation saves no profile");

    var first=await CreateDirect("Fixture A",$"http://127.0.0.1:{port}/a");
    var state=await Call("app.state"); var tab=Tabs(state).Single()["id"]!.GetValue<string>();
    await Until(async()=>
    {
        var browser=await Call("browser.state",new(){["tabId"]=tab});
        return browser["ready"]!.GetValue<bool>()&&browser["navigationSucceeded"]?.GetValue<bool>()==true;
    },"browser page loaded",20);
    Check(true,"connection editor and save/open are controlled entirely through the API");
    var scriptJob=await Call("browser.evaluate",new(){["tabId"]=tab,["script"]="prompt('fixture prompt','initial')"});
    await Find("SessionView.ScriptPromptBox");
    await Act("SessionView.ScriptPromptBox","setValue",new(){["value"]="api answer"});
    await Act("SessionView.ScriptDialogAccept","invoke");
    var scriptResult=await Job(scriptJob);
    Check(scriptResult["result"]?["value"]?.GetValue<string>()=="api answer","page prompt can be answered while another API request is pending");
    var cdp=await Job(await Call("browser.cdp",new(){["tabId"]=tab,["command"]="Runtime.evaluate",["parameters"]=new JsonObject{["expression"]="document.title",["returnByValue"]=true}}));
    Check(JsonNode.Parse(cdp["result"]!["json"]!.GetValue<string>())!["result"]!["value"]!.GetValue<string>()=="API fixture","public DevTools bridge reaches the actual embedded page");
    await Command("browser.evaluate",new(){["tabId"]=tab,["script"]="window.fixtureMarker='preserved'"});
    await Command("workspace.tab.rename",new(){["tabId"]=tab,["label"]="Renamed via API"});
    await Until(async()=>Tabs(await Call("app.state")).Single()["title"]!.GetValue<string>().Contains("Renamed via API"),"tab renamed");
    await Command("workspace.tab.split",new(){["tabId"]=tab,["direction"]="right"});
    state=await Call("app.state");
    Check(state["panes"]!.AsArray().Count==2&&Tabs(state).Count()==2,"semantic split retains both browser tabs");
    var sourcePane=state["panes"]!.AsArray().Single(p=>p!["tabs"]!.AsArray().Any(t=>t!["id"]!.GetValue<string>()==tab))!;
    var targetPane=state["panes"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>()!=sourcePane["id"]!.GetValue<string>())!;
    var targetId=targetPane["id"]!.GetValue<string>();
    await Command("workspace.pane.resize",new(){["paneId"]=targetId,["width"]="2*"});
    Check((await Call("app.state"))["panes"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>()==targetId)!["width"]!.GetValue<string>()=="2*","split resize commits the requested layout length");
    var pane=state["panes"]![0]!["id"]!.GetValue<string>();
    await Command("workspace.pane.maximize",new(){["paneId"]=pane});
    state=await Call("app.state");
    Check(state["paneMaximized"]!.GetValue<bool>()&&state["sidebarVisible"]!.GetValue<bool>(),"pane focus preserves connection sidebar");
    await Act("MainWindow.Window","key",new(){["value"]="Escape"});
    Check(!(await Call("app.state"))["paneMaximized"]!.GetValue<bool>(),"Escape semantic command restores pane layout");
    await Command("workspace.tab.move",new(){["tabId"]=tab,["paneId"]=targetId,["zone"]="center"});
    var marker=await Job(await Call("browser.evaluate",new(){["tabId"]=tab,["script"]="window.fixtureMarker"}));
    Check(marker["result"]?["value"]?.GetValue<string>()=="preserved","tab drag commit moves the existing browser without resetting page state");
    var tabNode=(await Call("ui.tree")).AsArray().Single(n=>n!["type"]!.GetValue<string>()=="DropDownControlArea"&&n["tabId"]?.GetValue<string>()==tab&&n["actions"]!.AsArray().Any(a=>a!.GetValue<string>()=="contextMenu"))!;
    await Job(await Call("ui.act",new(){["target"]=new JsonObject{["id"]=tabNode["id"]!.GetValue<string>()},["action"]="contextMenu"}));
    var menuItems=await Find("MainWindow.DuplicateTab_Click");
    Check(menuItems[0]!["enabled"]!.GetValue<bool>()&&menuItems[0]!["tabId"]?.GetValue<string>()==tab,"tab menu retains its real data context and tab identity");
    await Act("MainWindow.ContextMenu.1","dismiss");
    Check(true,"AvalonDock context menus are discoverable and operable through the public tree");
    await Command("workspace.layout.save",new(){["name"]="Fixture layout"});
    Check((await Call("workspace.layouts")).AsArray().Any(n=>n!["name"]!.GetValue<string>()=="Fixture layout"),"layout preset API persists the real workspace");
    var deletion=await Call("workspace.layout.delete",new(){["name"]="Fixture layout"});
    await Find("dialog.choice.no"); await Act("dialog.choice.no","invoke"); await Job(deletion);
    Check((await Call("workspace.layouts")).AsArray().Count==1,"negative confirmation does not delete layout");

    await Command("app.settings"); await Find("SettingsWindow.Categories");
    await Act("SettingsWindow.AppearanceBox","select",new(){["index"]=2});
    await Until(()=>Task.FromResult(JsonNode.Parse(File.ReadAllText(Path.Combine(data,"settings.json")))!["Appearance"]!.GetValue<string>()=="dark"),"appearance persisted");
    await Act("SettingsWindow.Categories","select",new(){["index"]=2});
    var export=await Call("ui.act",new(){["target"]=new JsonObject{["automationId"]="SettingsWindow.Export_Click"},["action"]="invoke"});
    await Find("dialog.file.path");
    var exported=Path.Combine(output,"exported.json");
    await Act("dialog.file.path","setValue",new(){["value"]=exported});
    await Act("dialog.file.accept","invoke"); await Job(export);
    await Until(()=>Task.FromResult(File.Exists(exported)),"export completed");
    Check(File.ReadAllText(exported).Contains("127.0.0.1"),"file picker and export complete without native-dialog automation");
    await Act("SettingsWindow.Categories","select",new(){["index"]=3});
    await Act("SettingsWindow.Expander","setExpanded",new(){["value"]=true});
    var sources=await Job(await Call("ui.act",new(){["target"]=new JsonObject{["automationId"]="SettingsWindow.SourceBox"},["action"]="items"}));
    Check(sources["result"]!.AsArray().Select(i=>i!["tag"]!.GetValue<string>()).Order().SequenceEqual(new[]{"cnb","github"}),"update UI exposes only CNB and GitHub");
    await Act("SettingsWindow.SourceAddressBox","setValue",new(){["value"]=@"\\obsolete\share"});
    await Act("SettingsWindow.SaveSource_Click","invoke");
    Check(JsonNode.Parse(File.ReadAllText(Path.Combine(data,"settings.json")))!["UpdateCnbUrl"]!.GetValue<string>()=="https://cnb.cool/railgunhamster/HarnessPortable","retired share paths cannot overwrite the saved CNB source");
    await Act("SettingsWindow.SourceBox","select",new(){["index"]=1});
    await Act("SettingsWindow.SaveSource_Click","invoke");
    await Until(()=>Task.FromResult(JsonNode.Parse(File.ReadAllText(Path.Combine(data,"settings.json")))!["UpdateSourceSelected"]!.GetValue<string>()=="github"),"GitHub alternative persisted");
    await Act("SettingsWindow.OfficialSource_Click","invoke");
    await Until(()=>Task.FromResult(JsonNode.Parse(File.ReadAllText(Path.Combine(data,"settings.json")))!["UpdateSourceSelected"]!.GetValue<string>()=="cnb"),"official CNB source restored");
    Check(true,"source switching and official reset use the same API-accessible UI operations");
    await Act("SettingsWindow.Window","window",new(){["value"]="close"});

    var second=await CreateDirect("Fixture B",$"http://127.0.0.1:{port}/b");
    await Command("library.favorite",new(){["key"]=second,["enabled"]=true});
    await Command("library.group",new(){["key"]=second,["group"]="Fixture group"});
    await Command("library.move",new(){["key"]=first,["targetKey"]=second,["after"]=true,["regroup"]=true});
    var entries=(await Call("library.list")).AsArray();
    Check(entries[0]!["key"]!.GetValue<string>()==second&&entries.All(n=>n!["group"]!.GetValue<string>()=="Fixture group"),"library reorder/regroup uses persisted connection service");
    var remove=await Call("library.delete",new(){["key"]=second});
    await Find("dialog.choice.no"); await Act("dialog.choice.no","invoke"); await Job(remove);
    Check((await Call("library.list")).AsArray().Count==2,"connection deletion cancellation preserves profile and tabs");
    remove=await Call("library.delete",new(){["key"]=second});
    await Find("dialog.choice.yes"); await Act("dialog.choice.yes","invoke"); await Job(remove);
    Check((await Call("library.list")).AsArray().Count==1,"confirmed connection deletion closes its tabs and removes the saved profile");
    var observed=await Call("events.read");
    Check(observed["events"]!.AsArray().Any(e=>e!["kind"]!.GetValue<string>()=="operation.returned")&&observed["events"]!.AsArray().Any(e=>e!["kind"]!.GetValue<string>()=="profiles.changed"),"event cursors report operations and persisted domain changes");
    await Command("connections.stopAll");
    var exit=await Call("tray.exit");
    Check(exit["operationId"] is not null,"terminal operation is acknowledged before the process exits");
    await product.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
    Check(product.ExitCode==0&&!File.Exists(endpoint),"application exit removes endpoint and leaves no background test process");
    File.WriteAllText(Path.Combine(output,"verification.json"),JsonSerializer.Serialize(new{passed=true,checks,methods=names.Count},new JsonSerializerOptions{WriteIndented=true}));
    Console.WriteLine("PASS: "+checks.Count+" cross-process API scenarios; "+names.Count+" methods.");
}
catch(Exception error)
{
    Console.Error.WriteLine(error);
    if(File.Exists(Path.Combine(data,"crash.log")))Console.Error.WriteLine(File.ReadAllText(Path.Combine(data,"crash.log")));
    Environment.ExitCode=1;
}
finally
{
    lifetime.Cancel();listener.Stop();
    if(!product.HasExited){product.Kill(entireProcessTree:true);await product.WaitForExitAsync();}
    try{await http;}catch(OperationCanceledException){}catch(SocketException){}
}

void Check(bool condition,string reason){if(!condition)throw new InvalidOperationException(reason);checks.Add(reason);Console.WriteLine("PASS: "+reason);}
IEnumerable<JsonObject> Tabs(JsonNode node)=>node["panes"]!.AsArray().SelectMany(p=>p!["tabs"]!.AsArray()).OfType<JsonObject>();
async Task<JsonNode> Call(string method,JsonObject? arguments=null)
{
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(35));
    await using var pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
    await pipe.ConnectAsync(timeout.Token);
    using var writer=new StreamWriter(pipe,new UTF8Encoding(false),4096,true){AutoFlush=true};using var reader=new StreamReader(pipe,new UTF8Encoding(false),false,4096,true);
    var request=new JsonObject{["version"]=1,["id"]=Guid.NewGuid().ToString("N"),["method"]=method,["params"]=arguments??new()};
    await writer.WriteLineAsync(request.ToJsonString().AsMemory(),timeout.Token);
    var response=JsonNode.Parse(await reader.ReadLineAsync(timeout.Token)??throw new IOException("API returned EOF."))!;
    if(response["error"] is not null)throw new InvalidOperationException(method+": "+response["error"]!.ToJsonString());
    return response["result"]??new JsonObject();
}
async Task<JsonNode> Job(JsonNode response)
{
    var id=response["operationId"]!.GetValue<string>(); JsonNode? result=null;
    await Until(async()=>{result=await Call("operations.get",new(){["operationId"]=id});return result["state"]!.GetValue<string>() is "returned" or "failed";},"operation returned",20);
    if(result!["state"]!.GetValue<string>()=="failed")throw new InvalidOperationException("Operation failed: "+result.ToJsonString());
    return result;
}
async Task Command(string method,JsonObject? arguments=null)=>await Job(await Call(method,arguments));
async Task<JsonNode> Find(string id)=>await Call("ui.wait",new(){["selector"]=new JsonObject{["automationId"]=id},["timeoutMs"]=15000});
async Task Act(string id,string action,JsonObject? values=null)
{
    var args=values??new();args["target"]=new JsonObject{["automationId"]=id};args["action"]=action;
    await Job(await Call("ui.act",args));
}
async Task<string> CreateDirect(string name,string url)
{
    var editor=await Call("library.create",new(){["kind"]="direct"});
    await Find("DirectConnectionWindow.UrlBox");
    await Act("DirectConnectionWindow.NameBox","setValue",new(){["value"]=name});
    await Act("DirectConnectionWindow.UrlBox","setValue",new(){["value"]=url});
    await Act("DirectConnectionWindow.Save_Click.connect","invoke"); await Job(editor);
    return (await Call("library.list")).AsArray().Single(n=>n!["name"]!.GetValue<string>()==name)!["key"]!.GetValue<string>();
}
async Task Until(Func<Task<bool>> condition,string reason,int seconds=10)
{
    var watch=Stopwatch.StartNew();
    while(!await condition()) {if(product.HasExited)throw new InvalidOperationException("Product exited while waiting for "+reason);if(watch.Elapsed>TimeSpan.FromSeconds(seconds))throw new TimeoutException(reason);await Task.Delay(40);}
}
static async Task ServeHttp(TcpListener listener,CancellationToken cancellation)
{
    while(!cancellation.IsCancellationRequested)
    {
        var client=await listener.AcceptTcpClientAsync(cancellation);
        _=Task.Run(async()=>
        {
            using(client)try
            {
                await using var stream=client.GetStream();var buffer=new byte[16384];var used=0;
                while(used<buffer.Length)
                {
                    var count=await stream.ReadAsync(buffer.AsMemory(used),cancellation);if(count==0)return;used+=count;
                    if(Encoding.ASCII.GetString(buffer,0,used).Contains("\r\n\r\n"))break;
                }
                var body=Encoding.UTF8.GetBytes("<!doctype html><title>API fixture</title><input id='value'><button id='button' onclick=\"document.body.dataset.clicked='yes'\">Test</button>");
                var header=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header,cancellation);await stream.WriteAsync(body,cancellation);
            }catch(Exception e)when(e is IOException or OperationCanceledException){}
        },cancellation);
    }
}
