using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AvalonDock.Layout;
using HarnessPortable.Windows.Controls;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows;

public partial class MainWindow : Window
{
    private enum SplitDirection
    {
        Left,
        Right,
        Up,
        Down,
    }

    private readonly AppServices _services;
    private readonly ManagementView _managementView;
    private readonly LayoutPresetStore _layoutPresets;

    private LayoutDocument _managementDoc = null!;

    private readonly Dictionary<string, TunnelProfile> _pendingProfiles = [];
    private readonly Dictionary<string, List<LayoutDocument>> _tunnelDocsByProfile = [];
    private readonly List<LayoutDocument> _directDocs = [];
    private readonly Dictionary<LayoutDocument, string> _docSuffixes = [];

    private LayoutDocument? _contextDoc;
    private bool _suppressPresetSelection;

    private bool _trayHintShown;
    private bool _isFullScreen;
    private WindowStyle _normalWindowStyle;
    private WindowState _preFullScreenState;
    private ResizeMode _normalResizeMode;
    private bool _normalTopmost;

    public MainWindow(AppServices services)
    {
        _services = services;
        _layoutPresets = new LayoutPresetStore();
        InitializeComponent();

        _managementView = new ManagementView(_services);
        _managementView.TunnelConnectRequested += ConnectTunnel;
        _managementView.TunnelStopRequested += CloseTunnelDocs;
        _managementView.DirectOpenRequested += OpenDirectSession;

        InitializeDockLayout();
        _services.Tunnels.StateChanged += OnTunnelStateChanged;
        RefreshStatusBar();
        RefreshPresetBox(null);
    }

    private void InitializeDockLayout()
    {
        _managementDoc = CreateManagementDoc();
        var pane = new LayoutDocumentPane(_managementDoc);
        var group = new LayoutDocumentPaneGroup(pane);
        DockManager.Layout = new LayoutRoot { RootPanel = new LayoutPanel(group) };
        _managementDoc.IsActive = true;
    }

    private LayoutDocument CreateManagementDoc() => new()
    {
        Title = "管理",
        ContentId = "management",
        CanClose = false,
        Content = _managementView,
    };

    private void OnTunnelStateChanged(TunnelInfo info)
    {
        if (Dispatcher.CheckAccess())
        {
            ApplyTunnelState(info);
        }
        else
        {
            Dispatcher.BeginInvoke(() => ApplyTunnelState(info));
        }
    }

    private void ApplyTunnelState(TunnelInfo info)
    {
        if (info.ProfileId is not null &&
            info.Status == TunnelStatus.Connected &&
            _pendingProfiles.TryGetValue(info.ProfileId, out var pending))
        {
            _pendingProfiles.Remove(info.ProfileId);
            OpenOrFocusTunnelSession(pending, info.LocalPort);
        }
        else if (info.ProfileId is not null &&
                 info.Status is TunnelStatus.Failed or TunnelStatus.Stopped)
        {
            _pendingProfiles.Remove(info.ProfileId);
        }

        RefreshStatusBar();
    }

    private void RefreshStatusBar()
    {
        var active = _services.Tunnels.GetActiveStates().ToList();
        StopTunnelButton.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        StatusText.Text = active.Count switch
        {
            0 => "就绪",
            _ => string.Join(
                "   |   ",
                active.Select(s => s.Status switch
                {
                    TunnelStatus.Connected => $"{s.ProfileName} 已连接 ({s.LocalPort})",
                    TunnelStatus.Connecting => $"{s.ProfileName} 连接中…",
                    _ => $"{s.ProfileName} 重连中…",
                })),
        };
    }

    // ------------------------------------------------------------------
    // Session views and tabs
    // ------------------------------------------------------------------

    private LayoutDocumentPane GetTargetPane()
    {
        var active = DockManager.Layout.ActiveContent;
        if (active?.Parent is LayoutDocumentPane activePane)
        {
            return activePane;
        }

        if (_managementDoc.Parent is LayoutDocumentPane managementPane)
        {
            return managementPane;
        }

        var pane = new LayoutDocumentPane(_managementDoc);
        var group = new LayoutDocumentPaneGroup(pane);
        DockManager.Layout.RootPanel = new LayoutPanel(group);
        return pane;
    }

    private void ConnectTunnel(TunnelProfile profile)
    {
        if (!EnsurePassword(profile))
        {
            return;
        }

        var state = _services.Tunnels.GetState(profile.Id);
        if (state.Status == TunnelStatus.Connected)
        {
            OpenOrFocusTunnelSession(profile, state.LocalPort);
            return;
        }

        _pendingProfiles[profile.Id] = profile;
        _services.Tunnels.Start(profile.Id);
    }

    private bool EnsurePassword(TunnelProfile profile)
    {
        if (_services.Secrets.HasPassword(profile.Id))
        {
            return true;
        }

        var prompt = new PasswordPromptWindow(profile) { Owner = this };
        if (prompt.ShowDialog() != true)
        {
            return false;
        }

        _services.Secrets.SetPassword(profile.Id, prompt.Password);
        return true;
    }

    private void OpenOrFocusTunnelSession(TunnelProfile profile, int localPort)
    {
        if (_tunnelDocsByProfile.TryGetValue(profile.Id, out var docs) && docs.Count > 0)
        {
            var doc = docs[0];
            doc.IsActive = true;
            doc.IsSelected = true;
            return;
        }

        CreateTunnelDoc(profile, localPort, GetTargetPane(), activate: true);
    }

    private SessionView CreateTunnelView(TunnelProfile profile, int localPort)
    {
        var view = new SessionView(_services, profile, localPort);
        WireSessionView(view);
        return view;
    }

    private SessionView CreateDirectView(string url)
    {
        var view = new SessionView(_services, url);
        WireSessionView(view);
        return view;
    }

    private void WireSessionView(SessionView view)
    {
        view.AppFullScreenActive = _isFullScreen;
        view.CloseRequested += () => CloseDocByView(view);
        view.FullScreenToggleRequested += ToggleFullScreen;
        view.EscapeRequested += ExitFullScreen;
    }

    private string NextTunnelSuffix(string profileId)
    {
        var count = _tunnelDocsByProfile.TryGetValue(profileId, out var docs) ? docs.Count : 0;
        return count == 0 ? "" : $" ({count + 1})";
    }

    private string NextDirectSuffix(string url)
    {
        var count = _directDocs.Count(d => (d.Content as SessionView)?.DirectUrl == url);
        return count == 0 ? "" : $" ({count + 1})";
    }

    private LayoutDocument CreateTunnelDoc(
        TunnelProfile profile,
        int localPort,
        LayoutDocumentPane targetPane,
        bool activate)
    {
        var suffix = NextTunnelSuffix(profile.Id);
        var view = CreateTunnelView(profile, localPort);

        var doc = new LayoutDocument
        {
            Title = view.SessionTitle + suffix,
            ContentId = $"tunnel:{profile.Id}:{Guid.NewGuid():N}",
            CanClose = true,
            Content = view,
        };

        _docSuffixes[doc] = suffix;

        view.TitleChanged += title =>
        {
            if (FindDocForView(view) is { } owner)
            {
                owner.Title = title + (_docSuffixes.TryGetValue(owner, out var s) ? s : "");
            }
        };

        doc.Closed += (_, _) => OnSessionDocClosed(doc);

        if (!_tunnelDocsByProfile.TryGetValue(profile.Id, out var target))
        {
            target = [];
            _tunnelDocsByProfile[profile.Id] = target;
        }

        target.Add(doc);
        targetPane.Children.Add(doc);

        if (activate)
        {
            doc.IsActive = true;
            doc.IsSelected = true;
        }

        return doc;
    }

    private LayoutDocument CreateDirectDoc(string url, string suffix, LayoutDocumentPane targetPane, bool activate)
    {
        var view = CreateDirectView(url);

        var doc = new LayoutDocument
        {
            Title = view.SessionTitle + suffix,
            ContentId = $"direct:{Guid.NewGuid():N}",
            CanClose = true,
            Content = view,
        };

        _docSuffixes[doc] = suffix;

        doc.Closed += (_, _) => OnSessionDocClosed(doc);

        _directDocs.Add(doc);
        targetPane.Children.Add(doc);

        if (activate)
        {
            doc.IsActive = true;
            doc.IsSelected = true;
        }

        return doc;
    }

    private void OpenDirectSession(string url)
    {
        CreateDirectDoc(url, NextDirectSuffix(url), GetTargetPane(), activate: true);
    }

    private void DuplicateViewInto(SessionView source, LayoutDocumentPane targetPane, bool activate)
    {
        if (source.IsTunnel && source.ProfileId is { } profileId)
        {
            var profile = _services.Profiles.FindTunnel(profileId);
            if (profile is not null)
            {
                CreateTunnelDoc(profile, source.LocalPort, targetPane, activate);
            }
        }
        else if (!source.IsTunnel && source.DirectUrl is { } url)
        {
            CreateDirectDoc(url, NextDirectSuffix(url), targetPane, activate);
        }
    }

    private LayoutDocument? FindDocForView(SessionView view)
    {
        foreach (var docs in _tunnelDocsByProfile.Values)
        {
            var match = docs.FirstOrDefault(d => ReferenceEquals(d.Content, view));
            if (match is not null)
            {
                return match;
            }
        }

        return _directDocs.FirstOrDefault(d => ReferenceEquals(d.Content, view));
    }

    private void CloseDocByView(SessionView view)
    {
        FindDocForView(view)?.Close();
    }

    private void OnSessionDocClosed(LayoutDocument doc)
    {
        _docSuffixes.Remove(doc);

        if (doc.Content is SessionView view)
        {
            view.Shutdown();
        }

        foreach (var list in _tunnelDocsByProfile.Values)
        {
            list.Remove(doc);
        }

        _directDocs.Remove(doc);
    }

    private void CloseTunnelDocs(string profileId)
    {
        if (!_tunnelDocsByProfile.TryGetValue(profileId, out var docs))
        {
            return;
        }

        foreach (var doc in docs.ToList())
        {
            doc.Close();
        }
    }

    private void CloseAllSessionDocs()
    {
        foreach (var docs in _tunnelDocsByProfile.Values)
        {
            foreach (var doc in docs.ToList())
            {
                doc.Close();
            }
        }

        foreach (var doc in _directDocs.ToList())
        {
            doc.Close();
        }
    }

    private void StopAll_Click(object sender, RoutedEventArgs e)
    {
        _pendingProfiles.Clear();
        _services.Tunnels.StopAll();
        CloseAllSessionDocs();
        RefreshStatusBar();
    }

    // ------------------------------------------------------------------
    // Tab context menu: duplicate / split / switch
    // ------------------------------------------------------------------

    private LayoutDocument? GetContextDocument(FrameworkElement source)
    {
        if (source.DataContext is AvalonDock.Controls.LayoutItem item &&
            item.LayoutElement is LayoutDocument doc)
        {
            return doc;
        }

        if (source.DataContext is LayoutDocument directDoc)
        {
            return directDoc;
        }

        return DockManager.Layout.ActiveContent as LayoutDocument;
    }

    private void DocumentContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.ContextMenu menu)
        {
            return;
        }

        _contextDoc = menu.PlacementTarget is FrameworkElement target
            ? GetContextDocument(target)
            : DockManager.Layout.ActiveContent as LayoutDocument;

        PopulateSwitchMenu();
    }

    private void PopulateSwitchMenu()
    {
        SwitchToMenu.Items.Clear();

        var current = _contextDoc;
        if (current is null || ReferenceEquals(current, _managementDoc) || current.Content is not SessionView currentView)
        {
            SwitchToMenu.IsEnabled = false;
            return;
        }

        SwitchToMenu.IsEnabled = true;

        foreach (var profile in _services.Profiles.LoadTunnels())
        {
            if (currentView.IsTunnel && currentView.ProfileId == profile.Id)
            {
                continue;
            }

            var item = new System.Windows.Controls.MenuItem
            {
                Header = $"隧道：{profile.DisplayName}",
                Tag = new LayoutTabRef { Kind = "tunnel", ProfileId = profile.Id },
            };
            item.Click += SwitchTab_Click;
            SwitchToMenu.Items.Add(item);
        }

        foreach (var url in _services.Profiles.LoadDirects())
        {
            if (!currentView.IsTunnel && currentView.DirectUrl == url)
            {
                continue;
            }

            var item = new System.Windows.Controls.MenuItem
            {
                Header = $"直连：{ProfileStore.HostOf(url)}",
                Tag = new LayoutTabRef { Kind = "direct", Url = url },
            };
            item.Click += SwitchTab_Click;
            SwitchToMenu.Items.Add(item);
        }

        if (SwitchToMenu.Items.Count == 0)
        {
            SwitchToMenu.IsEnabled = false;
        }
    }

    private void DuplicateTab_Click(object sender, RoutedEventArgs e)
    {
        var doc = GetContextDocumentFromMenuItem(sender);
        if (doc is null || ReferenceEquals(doc, _managementDoc) || doc.Content is not SessionView view)
        {
            return;
        }

        DuplicateViewInto(view, GetTargetPane(), activate: true);
    }

    private void SplitRight_Click(object sender, RoutedEventArgs e) =>
        SplitContextDocument(SplitDirection.Right, sender);

    private void SplitLeft_Click(object sender, RoutedEventArgs e) =>
        SplitContextDocument(SplitDirection.Left, sender);

    private void SplitDown_Click(object sender, RoutedEventArgs e) =>
        SplitContextDocument(SplitDirection.Down, sender);

    private void SplitUp_Click(object sender, RoutedEventArgs e) =>
        SplitContextDocument(SplitDirection.Up, sender);

    private void SplitContextDocument(SplitDirection direction, object menuItemSource)
    {
        var doc = GetContextDocumentFromMenuItem(menuItemSource) ?? _contextDoc;
        if (doc is null || ReferenceEquals(doc, _managementDoc) || doc.Content is not SessionView view)
        {
            return;
        }

        SplitDocument(doc, view, direction);
    }

    private void SplitDocument(LayoutDocument doc, SessionView view, SplitDirection direction)
    {
        if (doc.Parent is not LayoutDocumentPane oldPane || oldPane.Parent is not ILayoutContainer parent)
        {
            return;
        }

        var newPane = new LayoutDocumentPane();
        var group = new LayoutDocumentPaneGroup
        {
            Orientation = direction is SplitDirection.Left or SplitDirection.Right
                ? System.Windows.Controls.Orientation.Horizontal
                : System.Windows.Controls.Orientation.Vertical,
        };

        parent.ReplaceChild(oldPane, group);
        if (direction is SplitDirection.Left or SplitDirection.Up)
        {
            group.Children.Add(newPane);
            group.Children.Add(oldPane);
        }
        else
        {
            group.Children.Add(oldPane);
            group.Children.Add(newPane);
        }

        DuplicateViewInto(view, newPane, activate: true);
    }

    private void SwitchTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem item ||
            item.Tag is not LayoutTabRef target ||
            _contextDoc is not { } doc)
        {
            return;
        }

        SwitchDocumentTo(doc, target);
    }

    private void SwitchDocumentTo(LayoutDocument doc, LayoutTabRef target)
    {
        if (ReferenceEquals(doc, _managementDoc) || doc.Content is not SessionView oldView)
        {
            return;
        }

        if (target.Kind == "tunnel")
        {
            var profile = target.ProfileId is null ? null : _services.Profiles.FindTunnel(target.ProfileId);
            if (profile is null || !EnsurePassword(profile))
            {
                return;
            }

            var state = _services.Tunnels.GetState(profile.Id);
            var port = state.LocalPort > 0 ? state.LocalPort : profile.LocalPort;
            var newView = CreateTunnelView(profile, port);

            ReplaceDocumentContent(doc, oldView, newView, newTunnelProfileId: profile.Id);
            if (state.Status is not (TunnelStatus.Connected or TunnelStatus.Connecting or TunnelStatus.Retrying))
            {
                _services.Tunnels.Start(profile.Id);
            }
        }
        else if (target.Kind == "direct" && target.Url is { } url)
        {
            var newView = CreateDirectView(url);
            ReplaceDocumentContent(doc, oldView, newView, newTunnelProfileId: null);
        }
    }

    private void ReplaceDocumentContent(
        LayoutDocument doc,
        SessionView oldView,
        SessionView newView,
        string? newTunnelProfileId)
    {
        var suffix = _docSuffixes.TryGetValue(doc, out var existingSuffix) ? existingSuffix : "";

        // Detach old bookkeeping, then attach new content to the same doc.
        _docSuffixes.Remove(doc);
        if (oldView.ProfileId is { } oldProfileId &&
            _tunnelDocsByProfile.TryGetValue(oldProfileId, out var oldList))
        {
            oldList.Remove(doc);
        }

        _directDocs.Remove(doc);
        oldView.Shutdown();

        doc.Content = newView;
        doc.Title = newView.SessionTitle + suffix;
        doc.ContentId = newView.IsTunnel
            ? $"tunnel:{newView.ProfileId}:{Guid.NewGuid():N}"
            : $"direct:{Guid.NewGuid():N}";
        _docSuffixes[doc] = suffix;

        newView.TitleChanged += title =>
        {
            if (ReferenceEquals(doc.Content, newView))
            {
                doc.Title = title + suffix;
            }
        };

        if (newTunnelProfileId is { } profileId)
        {
            if (!_tunnelDocsByProfile.TryGetValue(profileId, out var list))
            {
                list = [];
                _tunnelDocsByProfile[profileId] = list;
            }

            list.Add(doc);
        }
        else
        {
            _directDocs.Add(doc);
        }
    }

    private LayoutDocument? GetContextDocumentFromMenuItem(object sender)
    {
        return sender is FrameworkElement element
            ? GetContextDocument(element)
            : _contextDoc;
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextDocumentFromMenuItem(sender) is { } doc)
        {
            doc.Close();
        }
    }

    // ------------------------------------------------------------------
    // Layout presets
    // ------------------------------------------------------------------

    private void SaveLayout_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LayoutNameWindow() { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var layout = new WorkspaceLayout
        {
            Name = dialog.LayoutName,
            SavedAt = DateTime.UtcNow,
            Root = CaptureLayoutNode(DockManager.Layout.RootPanel) ?? new LayoutNode { Kind = "pane" },
        };

        _layoutPresets.Upsert(layout);
        RefreshPresetBox(layout.Name);
    }

    private void DeleteLayout_Click(object sender, RoutedEventArgs e)
    {
        if (LayoutPresetBox.SelectedItem is not WorkspaceLayout selected)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            this,
            $"删除布局预设“{selected.Name}”吗？",
            "删除布局",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _layoutPresets.Delete(selected.Name);
        RefreshPresetBox(null);
    }

    private void LayoutPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetSelection || LayoutPresetBox.SelectedItem is not WorkspaceLayout layout)
        {
            return;
        }

        ApplyLayoutPreset(layout);
    }

    private void RefreshPresetBox(string? selectName)
    {
        var presets = _layoutPresets.Load();

        _suppressPresetSelection = true;
        LayoutPresetBox.ItemsSource = presets;
        LayoutPresetBox.SelectedItem = presets.FirstOrDefault(p =>
            string.Equals(p.Name, selectName, StringComparison.OrdinalIgnoreCase));
        _suppressPresetSelection = false;
    }

    private void ApplyLayoutPreset(WorkspaceLayout layout)
    {
        _pendingProfiles.Clear();
        CloseAllSessionDocs();

        _managementDoc = CreateManagementDoc();
        var managementPane = new LayoutDocumentPane(_managementDoc);

        var rootPanel = new LayoutPanel(managementPane);
        if (DeserializeLayoutNode(layout.Root) is { } body)
        {
            rootPanel.Children.Add(body);
        }

        DockManager.Layout = new LayoutRoot { RootPanel = rootPanel };

        var firstSession = AllSessionDocs().FirstOrDefault();
        if (firstSession is not null)
        {
            firstSession.IsActive = true;
            firstSession.IsSelected = true;
        }
        else
        {
            _managementDoc.IsActive = true;
        }

        RefreshStatusBar();
    }

    private IEnumerable<LayoutDocument> AllSessionDocs()
    {
        foreach (var docs in _tunnelDocsByProfile.Values)
        {
            foreach (var doc in docs)
            {
                yield return doc;
            }
        }

        foreach (var doc in _directDocs)
        {
            yield return doc;
        }
    }

    private LayoutNode? CaptureLayoutNode(ILayoutElement element)
    {
        switch (element)
        {
            case LayoutDocumentPane pane:
            {
                var tabs = pane.Children
                    .OfType<LayoutDocument>()
                    .Select(TabRefFromDoc)
                    .Where(t => t is not null)
                    .Select(t => t!)
                    .ToList();

                return tabs.Count == 0 ? null : new LayoutNode { Kind = "pane", Tabs = tabs };
            }

            case LayoutPanel panel:
            {
                var children = panel.Children
                    .Select(CaptureLayoutNode)
                    .Where(n => n is not null)
                    .Select(n => n!)
                    .ToList();

                return children.Count == 0
                    ? null
                    : new LayoutNode
                    {
                        Kind = "split",
                        Orientation = panel.Orientation == System.Windows.Controls.Orientation.Horizontal
                            ? "horizontal"
                            : "vertical",
                        Children = children,
                    };
            }

            case LayoutDocumentPaneGroup group:
            {
                var children = group.Children
                    .Select(CaptureLayoutNode)
                    .Where(n => n is not null)
                    .Select(n => n!)
                    .ToList();

                return children.Count == 0
                    ? null
                    : new LayoutNode
                    {
                        Kind = "split",
                        Orientation = group.Orientation == System.Windows.Controls.Orientation.Horizontal
                            ? "horizontal"
                            : "vertical",
                        Children = children,
                    };
            }

            default:
                return null;
        }
    }

    private LayoutTabRef? TabRefFromDoc(LayoutDocument doc)
    {
        if (ReferenceEquals(doc, _managementDoc))
        {
            return null;
        }

        return doc.Content is SessionView view
            ? view.IsTunnel && view.ProfileId is { } profileId
                ? new LayoutTabRef { Kind = "tunnel", ProfileId = profileId }
                : !view.IsTunnel && view.DirectUrl is { } url
                    ? new LayoutTabRef { Kind = "direct", Url = url }
                    : null
            : null;
    }

    private ILayoutPanelElement? DeserializeLayoutNode(LayoutNode node)
    {
        if (node.Kind == "pane")
        {
            var pane = new LayoutDocumentPane();
            foreach (var tab in node.Tabs)
            {
                CreateSessionForTab(tab, pane);
            }

            return pane.Children.Count == 0 ? null : pane;
        }

        var panel = new LayoutPanel
        {
            Orientation = node.Orientation == "vertical"
                ? System.Windows.Controls.Orientation.Vertical
                : System.Windows.Controls.Orientation.Horizontal,
        };

        foreach (var child in node.Children)
        {
            if (DeserializeLayoutNode(child) is { } element)
            {
                panel.Children.Add(element);
            }
        }

        return panel.Children.Count == 0 ? null : panel;
    }

    private void CreateSessionForTab(LayoutTabRef tab, LayoutDocumentPane pane)
    {
        if (tab.Kind == "tunnel")
        {
            var profile = tab.ProfileId is null ? null : _services.Profiles.FindTunnel(tab.ProfileId);
            if (profile is null || !EnsurePassword(profile))
            {
                return;
            }

            var state = _services.Tunnels.GetState(profile.Id);
            var port = state.LocalPort > 0 ? state.LocalPort : profile.LocalPort;
            CreateTunnelDoc(profile, port, pane, activate: false);

            if (state.Status is not (TunnelStatus.Connected or TunnelStatus.Connecting or TunnelStatus.Retrying))
            {
                _services.Tunnels.Start(profile.Id);
            }
        }
        else if (tab.Kind == "direct" && tab.Url is { } url)
        {
            CreateDirectDoc(url, NextDirectSuffix(url), pane, activate: false);
        }
    }

    // ------------------------------------------------------------------
    // Full screen
    // ------------------------------------------------------------------

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
        else if (_isFullScreen && e.Key == Key.Escape)
        {
            ExitFullScreen();
            e.Handled = true;
        }
    }

    private void ToggleFullScreen()
    {
        if (_isFullScreen)
        {
            ExitFullScreen();
        }
        else
        {
            EnterFullScreen();
        }
    }

    private void EnterFullScreen()
    {
        if (_isFullScreen)
        {
            return;
        }

        _isFullScreen = true;
        _normalWindowStyle = WindowStyle;
        _normalResizeMode = ResizeMode;
        _preFullScreenState = WindowState;
        _normalTopmost = Topmost;

        SetSessionFullScreenState(true);

        ChromeBar.Visibility = Visibility.Collapsed;
        ChromeStatus.Visibility = Visibility.Collapsed;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        WindowState = WindowState.Maximized;
    }

    private void ExitFullScreen()
    {
        if (!_isFullScreen)
        {
            return;
        }

        _isFullScreen = false;
        ChromeBar.Visibility = Visibility.Visible;
        ChromeStatus.Visibility = Visibility.Visible;

        SetSessionFullScreenState(false);

        WindowStyle = _normalWindowStyle;
        ResizeMode = _normalResizeMode;
        Topmost = _normalTopmost;
        WindowState = _preFullScreenState == WindowState.Minimized
            ? WindowState.Normal
            : _preFullScreenState;
    }

    private void SetSessionFullScreenState(bool fullScreen)
    {
        foreach (var view in AllSessionDocs().Select(d => d.Content).OfType<SessionView>())
        {
            view.AppFullScreenActive = fullScreen;
        }
    }

    // ------------------------------------------------------------------
    // Window lifecycle
    // ------------------------------------------------------------------

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        if (System.Windows.Application.Current is App { IsExiting: true })
        {
            return;
        }

        e.Cancel = true;
        Hide();

        if (!_trayHintShown)
        {
            _trayHintShown = true;
            System.Windows.Forms.MessageBox.Show(
                "Harness Portable 仍在托盘运行，已连接的隧道不会中断。\n双击托盘图标可重新打开窗口，右键图标可退出。",
                "Harness Portable",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Information);
        }
    }
}
