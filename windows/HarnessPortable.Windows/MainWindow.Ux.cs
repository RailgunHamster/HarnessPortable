using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AvalonDock.Layout;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace HarnessPortable.Windows;

public partial class MainWindow
{
    private const string WorkspaceSnapshotName = "上次工作区";
    private readonly WorkspaceSnapshotStore _workspaceSnapshots = new();
    private readonly DispatcherTimer _workspaceSaveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ConnectionLibrary? _connectionLibrary;
    private SettingsWindow? _settingsWindow;
    private bool _connectDuringRestore = true;
    private bool _workspaceReady;
    private string? _lastSnapshotRoot;
    private LayoutRoot? _observedWorkspaceRoot;
    private readonly List<LayoutDocument> _restoredSelectedDocuments = [];
    private LayoutDocument? _restoredActiveDocument;
    private readonly Dictionary<string, bool> _restoreConnectionReadiness = [];
    private Dictionary<string, TunnelProfile>? _restoreProfiles;
    private HashSet<string>? _restoreDirects;

    private bool CanConnectRestored(TunnelProfile profile)
    {
        if (!_connectDuringRestore) return false;
        if (_restoreConnectionReadiness.TryGetValue(profile.Id, out var ready)) return ready;
        ready = _workspaceReady ? EnsurePassword(profile) : _services.Secrets.HasPassword(profile.Id) || SshIdentity.HasUsableKey(profile.IdentityFile);
        _restoreConnectionReadiness[profile.Id] = ready;
        return ready;
    }

    private void InitializeWorkspaceUx()
    {
        _connectionLibrary = new(_services.Profiles, _services.Settings);
        ConnectionLibraryView.Initialize(_services);
        ConnectionLibraryView.OpenRequested += entry => { if (entry.Tunnel is { } profile) ConnectTunnel(profile); else if (entry.Url is { } url) OpenDirectSession(url); };
        ConnectionLibraryView.EditRequested += EditConnection;
        ConnectionLibraryView.DeleteRequested += DeleteConnection;
        ConnectionLibraryView.DisconnectRequested += entry => { if (entry.Tunnel is { } profile) _services.Tunnels.Stop(profile.Id); };
        ConnectionLibraryView.AddRequested += ShowAddMenu;
        ConnectionLibraryView.SettingsRequested += ShowSettings;
        ApplySidebarVisibility();
        _workspaceSaveTimer.Tick += (_, _) => { _workspaceSaveTimer.Stop(); SaveWorkspaceSnapshot(); };
        DockManager.LayoutChanged += (_, _) => ObserveWorkspaceRoot();
        ObserveWorkspaceRoot();
        Closed += (_, _) => { _workspaceSaveTimer.Stop(); _workspaceReady = false; if (_observedWorkspaceRoot is not null) _observedWorkspaceRoot.Updated -= WorkspaceModelUpdated; _settingsWindow?.Close(); };
        _workspaceReady = true;
        ScheduleWorkspaceSnapshot();
    }

    private void ObserveWorkspaceRoot()
    {
        if (_observedWorkspaceRoot is not null) _observedWorkspaceRoot.Updated -= WorkspaceModelUpdated;
        _observedWorkspaceRoot = DockManager.Layout;
        _observedWorkspaceRoot.Updated += WorkspaceModelUpdated;
        ScheduleWorkspaceSnapshot();
    }
    private void WorkspaceModelUpdated(object? sender, EventArgs e) => ScheduleWorkspaceSnapshot();
    private void ScheduleWorkspaceSnapshot()
    {
        if (!_workspaceReady) return;
        _workspaceSaveTimer.Stop(); _workspaceSaveTimer.Start();
    }

    private void ApplySidebarVisibility()
    {
        var collapsed = _isFullScreen || _services.Settings.Load().ConnectionSidebarCollapsed;
        SidebarColumn.Width = collapsed ? new GridLength(0) : new GridLength(260);
        SidebarBorder.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarToggleButton.Content = collapsed ? "☰ 连接库" : "☰ 收起侧栏";
    }
    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        var settings = _services.Settings.Load(); settings.ConnectionSidebarCollapsed = !settings.ConnectionSidebarCollapsed;
        _services.Settings.Save(settings); ApplySidebarVisibility();
    }
    private void ShowSettings()
    {
        BringToFront();
        if (_settingsWindow is null)
        {
            _settingsWindow = new(_services) { Owner = this };
            _settingsWindow.LibraryRequested += () =>
            {
                var settings = _services.Settings.Load(); settings.ConnectionSidebarCollapsed = false; _services.Settings.Save(settings); ApplySidebarVisibility();
            };
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else { if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal; _settingsWindow.Activate(); }
    }
    private void ShowAddMenu()
    {
        var menu = new ContextMenu { PlacementTarget = ConnectionLibraryView, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        var ssh = new MenuItem { Header = "SSH 隧道" }; ssh.Click += (_, _) => EditTunnel(null);
        var direct = new MenuItem { Header = "直连网页" }; direct.Click += (_, _) => EditDirect(null);
        menu.Items.Add(ssh); menu.Items.Add(direct); menu.IsOpen = true;
    }
    private void AddConnection_Click(object sender, RoutedEventArgs e) => ShowAddMenu();
    private void EditConnection(ConnectionEntry entry)
    {
        if (entry.Tunnel is { } profile) EditTunnel(profile); else EditDirect(entry.Url);
    }
    private void EditTunnel(TunnelProfile? profile)
    {
        var editor = new TunnelEditorWindow(profile, _services.Settings.Load().SshConfigPath,
            profile is null ? null : _services.Secrets.GetAuthInput(profile.Id)) { Owner = this };
        if (editor.ShowDialog() != true || editor.Result is not { } result) return;
        var profiles = _services.Profiles.LoadTunnels();
        var index = profiles.FindIndex(p => p.Id == result.Profile.Id);
        if (index >= 0) profiles[index] = result.Profile; else profiles.Insert(0, result.Profile);
        _services.Profiles.SaveTunnels(profiles);
        if (!string.IsNullOrEmpty(result.Password)) _services.Secrets.SetPassword(result.Profile.Id, result.Password);
        if (string.IsNullOrWhiteSpace(result.AuthInput)) _services.Secrets.ClearAuthInput(result.Profile.Id);
        else _services.Secrets.SetAuthInput(result.Profile.Id, result.AuthInput);
        var settings = _services.Settings.Load(); settings.SshConfigPath = result.SshConfigPath ?? ""; _services.Settings.Save(settings);
        if (editor.ConnectAfterSave)
        {
            if (!EnsurePassword(result.Profile)) { StatusText.Text = "配置已保存 · 未重新连接"; return; }
            _services.Tunnels.Stop(result.Profile.Id);
            ConnectTunnel(result.Profile);
        }
        else StatusText.Text = "连接已保存 · 当前连接保持，下次连接生效";
    }
    private void EditDirect(string? previous)
    {
        var settings = _services.Settings.Load();
        var oldKey = previous is null ? null : ConnectionLibrary.DirectKey(previous);
        var editor = new DirectConnectionWindow(previous, oldKey is null ? null : settings.Connections.GetValueOrDefault(oldKey),
            previous is null ? "none" : settings.DirectAuthModes.GetValueOrDefault(previous, "none")) { Owner = this };
        if (editor.ShowDialog() != true) return;
        var directs = _services.Profiles.LoadDirects();
        if (previous is not null) directs.Remove(previous);
        directs.Remove(editor.Url); directs.Insert(0, editor.Url); _services.Profiles.SaveDirects(directs);
        settings = _services.Settings.Load();
        var lastOpened = oldKey is null ? null : settings.Connections.GetValueOrDefault(oldKey)?.LastOpened;
        if (oldKey is not null) settings.Connections.Remove(oldKey);
        if (oldKey is not null)
            settings.ConnectionOrder = settings.ConnectionOrder.Select(key => key == oldKey ? ConnectionLibrary.DirectKey(editor.Url) : key).Distinct().ToList();
        if (previous is not null) settings.DirectAuthModes.Remove(previous);
        editor.Preference.LastOpened = lastOpened;
        settings.Connections[ConnectionLibrary.DirectKey(editor.Url)] = editor.Preference;
        settings.DirectAuthModes[editor.Url] = editor.AuthMode;
        _services.Settings.Save(settings);
        if (editor.OpenAfterSave) OpenDirectSession(editor.Url);
        else StatusText.Text = "连接已保存 · 下次打开网页生效";
    }
    private void DeleteConnection(ConnectionEntry entry)
    {
        if (System.Windows.MessageBox.Show(this, $"删除“{entry.Name}”的连接配置和已保存凭据？该连接的网页也会关闭。", "删除连接",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        if (entry.Tunnel is { } profile)
        {
            _pendingProfiles.Remove(profile.Id); _services.Tunnels.Stop(profile.Id); CloseTunnelDocs(profile.Id);
            _services.Profiles.SaveTunnels(_services.Profiles.LoadTunnels().Where(p => p.Id != profile.Id));
            _services.Secrets.ClearPassword(profile.Id); _services.Secrets.ClearAuthInput(profile.Id);
        }
        else if (entry.Url is { } url)
        {
            foreach (var doc in _directDocs.Where(d => (d.Content as Controls.SessionView)?.DirectUrl == url).ToList()) doc.Close();
            _services.Profiles.SaveDirects(_services.Profiles.LoadDirects().Where(u => u != url));
        }
        var settings = _services.Settings.Load(); settings.Connections.Remove(entry.Key);
        settings.ConnectionOrder.RemoveAll(key => key == entry.Key);
        if (entry.Url is not null) settings.DirectAuthModes.Remove(entry.Url);
        _services.Settings.Save(settings);
    }
    private void SplitWorkspace_Click(object sender, RoutedEventArgs e)
    {
        if (DockManager.Layout.ActiveContent is not LayoutDocument { Content: Controls.SessionView view } doc)
        { StatusText.Text = "先打开一个服务，然后选择分屏方向"; return; }
        var menu = new ContextMenu { PlacementTarget = sender as UIElement };
        foreach (var (title, direction) in new[] { ("向右分屏", SplitDirection.Right), ("向左分屏", SplitDirection.Left), ("向下分屏", SplitDirection.Down), ("向上分屏", SplitDirection.Up) })
        { var item = new MenuItem { Header = title }; item.Click += (_, _) => SplitDocument(doc, view, direction); menu.Items.Add(item); }
        menu.IsOpen = true;
    }
    private void SaveWorkspaceSnapshot()
    {
        if (!_workspaceReady) return;
        try
        {
            var root = CaptureLayoutNode(DockManager.Layout.RootPanel) ?? new LayoutNode { Kind = "pane" };
            var serialized = JsonSerializer.Serialize(root);
            if (serialized == _lastSnapshotRoot) return;
            _workspaceSnapshots.Save(new WorkspaceLayout { Name = WorkspaceSnapshotName, SavedAt = DateTime.UtcNow, Root = root });
            _lastSnapshotRoot = serialized;
        }
        catch (Exception error) { FlickerLog.Log("workspace", "snapshot failed: " + error.GetType().Name); }
    }
}
