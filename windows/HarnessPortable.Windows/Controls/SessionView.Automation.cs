using System.IO;
using System.Text.Json.Nodes;
using HarnessPortable.Windows.Automation;
using Microsoft.Web.WebView2.Core;

namespace HarnessPortable.Windows.Controls;

public partial class SessionView
{
    public string? AutomationTabId { get; internal set; }
    private bool _automationNavigating;
    private bool? _automationNavigationSucceeded;
    public JsonObject CaptureAutomationBrowserState() => new()
    {
        ["ready"]=_coreReady&&!_shutdown&&!_browserFailed, ["navigating"]=_automationNavigating,
        ["navigationSucceeded"]=_automationNavigationSucceeded, ["parked"]=_pageParked,
        ["url"]=CurrentUrl is { } url ? Services.DiagnosticUrls.Redact(url) : null,
        ["scriptDialogOpen"]=ScriptDialogOverlay.Visibility==System.Windows.Visibility.Visible
    };

    public async Task<JsonNode?> ExecuteAutomationAsync(string operation, JsonObject arguments)
    {
        switch (operation)
        {
            case "reload": await RefreshAsync(); break;
            case "authenticate": RequestAuthentication(); break;
            case "reconnect": RequestReconnect(); break;
            case "disconnect": RequestDisconnect(); break;
            case "evaluate":
                if (WebView.CoreWebView2 is null) throw new AutomationFault("unavailable", "Browser is not initialized.");
                var result = await WebView.CoreWebView2.ExecuteScriptAsync(arguments.String("script")).WaitAsync(TimeSpan.FromSeconds(10));
                return arguments.Boolean("raw") ? new JsonObject{["json"]=result} : new JsonObject { ["value"] = JsonNode.Parse(result,documentOptions:new(){MaxDepth=48}) };
            case "cdp":
                if(WebView.CoreWebView2 is null)throw new AutomationFault("unavailable","Browser is not initialized.");
                var response=await WebView.CoreWebView2.CallDevToolsProtocolMethodAsync(arguments.String("command"),(arguments["parameters"] as JsonObject)?.ToJsonString()??"{}").WaitAsync(TimeSpan.FromSeconds(10));
                return new JsonObject{["json"]=response};
            case "capture":
                if (WebView.CoreWebView2 is null) throw new AutomationFault("unavailable", "Browser is not initialized.");
                using (var output = new MemoryStream())
                {
                    await WebView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, output).WaitAsync(TimeSpan.FromSeconds(10));
                    return new JsonObject { ["mimeType"] = "image/png", ["base64"] = Convert.ToBase64String(output.ToArray()) };
                }
            default: throw new AutomationFault("unsupported", "Browser operation is not registered.");
        }
        return new JsonObject { ["dispatched"] = true };
    }
}
