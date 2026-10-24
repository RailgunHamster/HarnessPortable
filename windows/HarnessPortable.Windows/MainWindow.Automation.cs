using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using AvalonDock.Layout;
using HarnessPortable.Windows.Automation;
using HarnessPortable.Windows.Controls;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows;

public partial class MainWindow
{
    private sealed record PaneIdentity(string Id);
    private readonly ConditionalWeakTable<LayoutDocumentPane, PaneIdentity> _apiPaneIds = new();
    private string ApiPaneId(LayoutDocumentPane pane) => _apiPaneIds.GetValue(pane, _ => new(Guid.NewGuid().ToString("N"))).Id;
    private LayoutDocumentPane ApiPane(string id) => DockManager.Layout.Descendents().OfType<LayoutDocumentPane>()
        .SingleOrDefault(p => ApiPaneId(p) == id) ?? throw new AutomationFault("not_found", "Pane no longer exists.");
    private LayoutDocument ApiTab(string id) => AllSessionDocs().SingleOrDefault(d => d.ContentId == id)
        ?? throw new AutomationFault("not_found", "Tab no longer exists.");
    private static string ApiLength(GridLength length)=>length.IsAuto ? "auto" : length.Value.ToString(CultureInfo.InvariantCulture)+(length.IsStar ? "*" : "");
    private ConnectionEntry ApiConnection(string key) => _connectionLibrary?.Load().SingleOrDefault(c => AutomationKeys.Connection(c) == key)
        ?? throw new AutomationFault("not_found", "Connection no longer exists.");

    public void RegisterAutomation(UiAutomationService surface)
    {
        void ProfilesChanged()=>surface.Events.Publish("profiles.changed");
        void SettingsChanged()=>surface.Events.Publish("settings.changed");
        void UpdatesChanged()=>surface.Events.Publish("updates.changed",new(){["busy"]=_services.Updates.Busy,["progress"]=_services.Updates.Progress,["canApply"]=_services.Updates.CanApply});
        void TunnelChanged(TunnelInfo info)=>surface.Events.Publish("tunnel.changed",new(){["profileId"]=info.ProfileId,["status"]=info.Status.ToString().ToLowerInvariant(),["localPort"]=info.LocalPort});
        surface.Track(()=>_services.Profiles.Changed+=ProfilesChanged,()=>_services.Profiles.Changed-=ProfilesChanged);
        surface.Track(()=>_services.Settings.Changed+=SettingsChanged,()=>_services.Settings.Changed-=SettingsChanged);
        surface.Track(()=>_services.Updates.StateChanged+=UpdatesChanged,()=>_services.Updates.StateChanged-=UpdatesChanged);
        surface.Track(()=>_services.Tunnels.StateChanged+=TunnelChanged,()=>_services.Tunnels.StateChanged-=TunnelChanged);
        void Command(string name, string description, string[] parameters, Action<JsonObject> action, string[]? optional = null) => surface.RegisterCommand(name, description, parameters, action, optional);
        surface.RegisterQuery("app.state", "Read window, workspace, connection and update state; credentials and URL queries are omitted.", [], _ => CaptureAutomationState());
        surface.RegisterQuery("library.list", "List saved connections and their stable keys.", [], _ => JsonSerializer.SerializeToNode(
            _connectionLibrary?.Load().Select(c => new { key=AutomationKeys.Connection(c), c.Name, address=c.Url is null ? c.Address : DiagnosticUrls.Redact(c.Address), c.Favorite, c.Group, c.Order,
                tunnelStatus=c.Tunnel is null ? null : _services.Tunnels.GetState(c.Tunnel.Id).Status.ToString().ToLowerInvariant() }), AutomationApi.JsonOptions));
        surface.RegisterQuery("workspace.layouts", "List saved workspace preset names and timestamps.", [], _ => JsonSerializer.SerializeToNode(_layoutPresets.Load().Select(l => new { l.Name, l.SavedAt }), AutomationApi.JsonOptions));
        surface.RegisterQuery("browser.state", "Read initialization, navigation, parked-page and script-dialog state.", ["tabId"], a => ((SessionView)ApiTab(a.String("tabId")).Content).CaptureAutomationBrowserState());
        Command("app.show", "Show the main window, including when hidden to the tray.", [], _ => BringToFront());
        Command("app.settings", "Open the settings window.", [], _ => ShowSettings());
        Command("app.fullscreen", "Set application full-screen state.", ["enabled"], a => { if (a.Boolean("enabled")) EnterFullScreen(); else ExitFullScreen(); });
        Command("app.escape", "Cancel tab dragging, restore the pane, or exit full screen, in UI shortcut order.", [], _ =>
        { if (_tabDragActive) EndTabDrag(); else if (IsPaneMaximized) RestorePaneFocus(); else ExitFullScreen(); });
        Command("connections.stopAll", "Stop all tunnels while retaining tabs.", [], _ => _services.Tunnels.StopAll());
        Command("library.create", "Open the same SSH/direct editor used by the connection library.", ["kind"], a =>
        {
            switch (a.String("kind")) { case "tunnel": EditTunnel(null); break; case "direct": EditDirect(null); break;
                default: throw new AutomationFault("invalid_arguments", "kind must be tunnel or direct."); }
        });
        Command("library.open", "Open or focus a saved connection; missing credentials use the normal password dialog.", ["key"], a =>
        { var entry = ApiConnection(a.String("key")); if (entry.Tunnel is { } tunnel) ConnectTunnel(tunnel); else OpenDirectSession(entry.Url!); });
        Command("library.edit", "Open the same connection editor used by the UI.", ["key"], a => EditConnection(ApiConnection(a.String("key"))));
        Command("library.delete", "Request deletion through the normal confirmation dialog.", ["key"], a => DeleteConnection(ApiConnection(a.String("key"))));
        Command("library.favorite", "Set favorite using the same connection-library service.", ["key", "enabled"], a =>
        { var entry = ApiConnection(a.String("key")); _connectionLibrary!.Update(entry.Key, p => p.Favorite = a.Boolean("enabled")); });
        Command("library.group", "Set connection group using the same connection-library service.", ["key", "group"], a =>
        { var entry = ApiConnection(a.String("key")); _connectionLibrary!.Update(entry.Key, p => p.Group = a.String("group").Trim()); });
        Command("library.move", "Reorder/regroup a connection using the same operation as sidebar drag-and-drop.", ["key", "targetKey", "after", "regroup"], a =>
        { if (!_connectionLibrary!.Move(ApiConnection(a.String("key")).Key, ApiConnection(a.String("targetKey")).Key, a.Boolean("after"), a.Boolean("regroup"))) throw new AutomationFault("conflict", "Connection move is not applicable."); }, ["after", "regroup"]);
        Command("library.addMenu", "Open the connection-library blank-area add menu.", [], _ => ShowAddMenu());
        Command("workspace.tab.select", "Activate a tab and restore a hidden focused pane when necessary.", ["tabId"], a => ApiTab(a.String("tabId")).IsActive = true);
        Command("workspace.tab.close", "Close a tab through its normal close lifecycle.", ["tabId"], a => ApiTab(a.String("tabId")).Close());
        Command("workspace.tab.rename", "Set the tab label using the same rename operation as the UI.", ["tabId", "label"], a => RenameDocument(ApiTab(a.String("tabId")), a.String("label")));
        Command("workspace.tab.duplicate", "Duplicate a session into a pane while retaining the original.", ["tabId", "paneId"], a =>
        { var doc = ApiTab(a.String("tabId")); DuplicateViewInto((SessionView)doc.Content, a.OptionalString("paneId") is { } pane ? ApiPane(pane) : GetTargetPane(), true); }, ["paneId"]);
        Command("workspace.tab.split", "Split a session left/right/up/down using the same operation as the tab menu.", ["tabId", "direction"], a =>
        {
            var doc = ApiTab(a.String("tabId"));
            if (!Enum.TryParse<SplitDirection>(a.String("direction"), true, out var direction) || !Enum.IsDefined(direction)) throw new AutomationFault("invalid_arguments", "Invalid split direction.");
            SplitDocument(doc, (SessionView)doc.Content, direction);
        });
        Command("workspace.tab.move", "Move/split an existing tab using the same commit operation as tab drag-and-drop.", ["tabId", "paneId", "zone"], a =>
        {
            if (!Enum.TryParse<TabDropZone>(a.String("zone"), true, out var zone) || !Enum.IsDefined(zone)) throw new AutomationFault("invalid_arguments", "Invalid tab-drop zone.");
            RunTabDrop(ApiTab(a.String("tabId")), ApiPane(a.String("paneId")), zone);
        });
        Command("workspace.tab.switch", "Replace a tab with a saved connection using the same operation as the switch menu.", ["tabId", "key"], a =>
        {
            var entry = ApiConnection(a.String("key"));
            SwitchDocumentTo(ApiTab(a.String("tabId")), entry.Tunnel is { } tunnel ? new() { Kind="tunnel", ProfileId=tunnel.Id } : new() { Kind="direct", Url=entry.Url });
        });
        Command("workspace.pane.maximize", "Expand a pane within the workspace, preserving the connection sidebar.", ["paneId"], a => MaximizePane(ApiPane(a.String("paneId"))));
        Command("workspace.pane.restore", "Restore the original split sizes.", [], _ => RestorePaneFocus());
        Command("workspace.pane.resize", "Set split lengths (for example 2* or 320) without moving browser hosts.", ["paneId", "width", "height"], a =>
        {
            if (a["width"] is null && a["height"] is null) throw new AutomationFault("invalid_arguments", "Provide width or height.");
            if (IsPaneMaximized) throw new AutomationFault("conflict", "Restore the focused pane before changing saved split lengths.");
            var pane = ApiPane(a.String("paneId"));
            GridLength Parse(string name)
            {
                var text=a.String(name).Trim();var star=text.EndsWith('*');
                var number=star ? text[..^1] : text;
                if(star&&number.Length==0)number="1";
                if(!double.TryParse(number,NumberStyles.Float,CultureInfo.InvariantCulture,out var size)||!double.IsFinite(size)||size<=0)
                    throw new AutomationFault("invalid_arguments", "Split length must be positive pixels or star units.");
                return new GridLength(size,star ? GridUnitType.Star : GridUnitType.Pixel);
            }
            var width = a["width"] is null ? pane.DockWidth : Parse("width"); var height = a["height"] is null ? pane.DockHeight : Parse("height");
            pane.DockWidth = width; pane.DockHeight = height;
        }, ["width", "height"]);
        Command("workspace.layout.save", "Save a named preset, including original split sizes while focused.", ["name"], a => SaveLayoutPreset(a.String("name")));
        Command("workspace.layout.load", "Load a named preset through the existing restore path.", ["name"], a => ApplyLayoutPreset(_layoutPresets.Load().FirstOrDefault(l => l.Name == a.String("name")) ?? throw new AutomationFault("not_found", "Layout does not exist.")));
        Command("workspace.layout.delete", "Delete a named preset through the normal confirmation dialog.", ["name"], a => DeleteLayoutPreset(_layoutPresets.Load().FirstOrDefault(l => l.Name == a.String("name")) ?? throw new AutomationFault("not_found", "Layout does not exist.")));
        surface.RegisterCommand("updates.check", "Check the saved update source and wait for the check result.", [], async _ =>
        { await _services.Updates.CheckAsync(UpdateSelection.From(_services.Settings.Load()).Url()); return CaptureAutomationState(); });
        surface.RegisterCommand("updates.apply", "Apply an available update through the normal updater.", [], async _ =>
        { await _services.Updates.ApplyAsync(UpdateSelection.From(_services.Settings.Load()).Url()); return CaptureAutomationState(); });
        foreach (var operation in new[] { "reload", "authenticate", "reconnect", "disconnect", "evaluate", "capture", "cdp" })
        {
            var name = "browser." + operation;
            surface.RegisterCommand(name, "Operate on a tab's existing browser/session; no browser host is re-created.",
                operation == "evaluate" ? ["tabId", "script", "raw"] : operation=="cdp" ? ["tabId","command","parameters"] : ["tabId"], async a =>
                    await ((SessionView)ApiTab(a.String("tabId")).Content).ExecuteAutomationAsync(operation, a),operation=="evaluate" ? ["raw"] : operation=="cdp" ? ["parameters"] : null);
        }
    }

    public JsonObject CaptureAutomationState() => new()
    {
        ["version"] = AppVersion.Current, ["fullScreen"] = _isFullScreen, ["paneMaximized"] = IsPaneMaximized,
        ["sidebarVisible"] = SidebarBorder.Visibility == Visibility.Visible,
        ["panes"] = new JsonArray(DockManager.Layout.Descendents().OfType<LayoutDocumentPane>().Select(p => (JsonNode?)new JsonObject
        {
            ["id"] = ApiPaneId(p), ["width"] = ApiLength(p.DockWidth), ["height"] = ApiLength(p.DockHeight),
            ["tabs"] = new JsonArray(p.Children.Select(d => (JsonNode?)new JsonObject
            {
                ["id"] = d.ContentId, ["title"] = d.Title, ["selected"] = d.IsSelected, ["active"] = d.IsActive,
                ["url"] = d.Content is SessionView { CurrentUrl: { } url } ? DiagnosticUrls.Redact(url) : null,
                ["profileId"] = (d.Content as SessionView)?.ProfileId
            }).ToArray())
        }).ToArray()),
        ["tunnels"] = JsonSerializer.SerializeToNode(_services.Profiles.LoadTunnels().Select(p=>_services.Tunnels.GetState(p.Id)).Select(t => new { t.ProfileId, status=t.Status.ToString().ToLowerInvariant(), t.LocalPort }), AutomationApi.JsonOptions),
        ["updates"] = new JsonObject { ["busy"] = _services.Updates.Busy, ["availableVersion"] = _services.Updates.AvailableVersion, ["canApply"] = _services.Updates.CanApply, ["progress"] = _services.Updates.Progress }
    };
}
