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
    private readonly AppServices _services;
    private readonly ManagementView _managementView;
    private readonly LayoutDocument _managementDoc;

    private readonly Dictionary<string, TunnelProfile> _pendingProfiles = [];
    private readonly Dictionary<string, List<LayoutDocument>> _tunnelDocsByProfile = [];
    private readonly List<LayoutDocument> _directDocs = [];
    private readonly Dictionary<LayoutDocument, string> _docSuffixes = [];

    private bool _trayHintShown;
    private bool _isFullScreen;
    private WindowStyle _normalWindowStyle;
    private WindowState _preFullScreenState;
    private ResizeMode _normalResizeMode;
    private bool _normalTopmost;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        _managementView = new ManagementView(_services);
        _managementView.TunnelConnectRequested += ConnectTunnel;
        _managementView.TunnelStopRequested += CloseTunnelDocs;
        _managementView.DirectOpenRequested += OpenDirectSession;

        _managementDoc = new LayoutDocument
        {
            Title = "管理",
            ContentId = "management",
            CanClose = false,
            Content = _managementView,
        };

        InitializeDockLayout();
        _services.Tunnels.StateChanged += OnTunnelStateChanged;
        RefreshStatusBar();
    }

    private void InitializeDockLayout()
    {
        var pane = new LayoutDocumentPane(_managementDoc);
        var group = new LayoutDocumentPaneGroup(pane);
        var layout = new LayoutRoot { RootPanel = new LayoutPanel(group) };
        DockManager.Layout = layout;
        _managementDoc.IsActive = true;
    }

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
    // Session tabs
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
        if (!_services.Secrets.HasPassword(profile.Id))
        {
            var prompt = new PasswordPromptWindow(profile) { Owner = this };
            if (prompt.ShowDialog() == true)
            {
                _services.Secrets.SetPassword(profile.Id, prompt.Password);
            }
            else
            {
                return;
            }
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

    private void OpenOrFocusTunnelSession(TunnelProfile profile, int localPort)
    {
        if (_tunnelDocsByProfile.TryGetValue(profile.Id, out var docs) && docs.Count > 0)
        {
            var doc = docs[0];
            doc.IsActive = true;
            doc.IsSelected = true;
            return;
        }

        CreateTunnelSession(profile, localPort);
    }

    private void CreateTunnelSession(TunnelProfile profile, int localPort)
    {
        var existingCount = _tunnelDocsByProfile.TryGetValue(profile.Id, out var docs) ? docs.Count : 0;
        var suffix = existingCount == 0 ? "" : $" ({existingCount + 1})";

        var view = new SessionView(_services, profile, localPort);
        view.AppFullScreenActive = _isFullScreen;
        view.CloseRequested += () => CloseDocByView(view);
        view.FullScreenToggleRequested += ToggleFullScreen;
        view.EscapeRequested += ExitFullScreen;
        view.TitleChanged += title =>
        {
            if (FindDocForView(view) is { } doc)
            {
                doc.Title = title + suffix;
            }
        };

        var doc = new LayoutDocument
        {
            Title = view.SessionTitle + suffix,
            ContentId = $"tunnel:{profile.Id}:{Guid.NewGuid():N}",
            CanClose = true,
            Content = view,
        };

        _docSuffixes[doc] = suffix;

        doc.Closed += (_, _) =>
        {
            _docSuffixes.Remove(doc);
            view.Shutdown();
            if (_tunnelDocsByProfile.TryGetValue(profile.Id, out var list))
            {
                list.Remove(doc);
            }
        };

        if (!_tunnelDocsByProfile.TryGetValue(profile.Id, out var target))
        {
            target = [];
            _tunnelDocsByProfile[profile.Id] = target;
        }

        target.Add(doc);
        GetTargetPane().Children.Add(doc);
        doc.IsActive = true;
        doc.IsSelected = true;
    }

    private void OpenDirectSession(string url) => CreateDirectSession(url, string.Empty);

    private void CreateDirectSession(string url, string suffix)
    {
        var view = new SessionView(_services, url);
        view.AppFullScreenActive = _isFullScreen;
        view.CloseRequested += () => CloseDocByView(view);
        view.FullScreenToggleRequested += ToggleFullScreen;
        view.EscapeRequested += ExitFullScreen;

        var doc = new LayoutDocument
        {
            Title = view.SessionTitle + suffix,
            ContentId = $"direct:{Guid.NewGuid():N}",
            CanClose = true,
            Content = view,
        };

        _docSuffixes[doc] = suffix;

        doc.Closed += (_, _) =>
        {
            _docSuffixes.Remove(doc);
            _directDocs.Remove(doc);
            view.Shutdown();
        };

        _directDocs.Add(doc);
        GetTargetPane().Children.Add(doc);
        doc.IsActive = true;
        doc.IsSelected = true;
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

    private void StopAll_Click(object sender, RoutedEventArgs e)
    {
        _pendingProfiles.Clear();
        _services.Tunnels.StopAll();

        foreach (var docs in _tunnelDocsByProfile.Values)
        {
            foreach (var doc in docs.ToList())
            {
                doc.Close();
            }
        }

        RefreshStatusBar();
    }

    // ------------------------------------------------------------------
    // Tab context menu
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

    private void DuplicateTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem menuItem)
        {
            return;
        }

        var doc = GetContextDocument(menuItem);
        if (doc is null || ReferenceEquals(doc, _managementDoc) || doc.Content is not SessionView view)
        {
            return;
        }

        if (view.IsTunnel && view.ProfileId is { } profileId)
        {
            var profile = _services.Profiles.FindTunnel(profileId);
            if (profile is not null)
            {
                CreateTunnelSession(profile, view.LocalPort);
            }
        }
        else if (!view.IsTunnel && view.DirectUrl is { } url)
        {
            var existingCount = _directDocs.Count(d =>
                (d.Content as SessionView)?.DirectUrl == url);

            CreateDirectSession(url, existingCount == 0 ? "" : $" ({existingCount + 1})");
        }
    }

    private void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            GetContextDocument(menuItem)?.Close();
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
        foreach (var docs in _tunnelDocsByProfile.Values)
        {
            foreach (var doc in docs)
            {
                if (doc.Content is SessionView view)
                {
                    view.AppFullScreenActive = fullScreen;
                }
            }
        }

        foreach (var doc in _directDocs)
        {
            if (doc.Content is SessionView view)
            {
                view.AppFullScreenActive = fullScreen;
            }
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

        // Desktop convention: closing the window keeps active tunnels alive
        // in the tray; exit explicitly from the tray menu.
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
