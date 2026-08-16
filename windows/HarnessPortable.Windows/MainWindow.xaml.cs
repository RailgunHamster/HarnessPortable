using System.Windows;
using System.Windows.Controls;
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
    private readonly Dictionary<string, LayoutDocument> _tunnelDocs = [];
    private readonly List<LayoutDocument> _directDocs = [];

    private bool _trayHintShown;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        _managementView = new ManagementView(_services);
        _managementView.TunnelConnectRequested += ConnectTunnel;
        _managementView.TunnelStopRequested += CloseTunnelDoc;
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
        if (_tunnelDocs.TryGetValue(profile.Id, out var existing))
        {
            existing.IsActive = true;
            existing.IsSelected = true;
            return;
        }

        var view = new SessionView(_services, profile, localPort);
        view.CloseRequested += () => CloseDocByView(view);
        view.TitleChanged += title =>
        {
            if (_tunnelDocs.TryGetValue(profile.Id, out var doc))
            {
                doc.Title = title;
            }
        };

        var doc = new LayoutDocument
        {
            Title = view.SessionTitle,
            ContentId = $"tunnel:{profile.Id}",
            CanClose = true,
            Content = view,
        };

        doc.Closed += (_, _) =>
        {
            if (_tunnelDocs.Remove(profile.Id))
            {
                view.Shutdown();
            }
        };

        _tunnelDocs[profile.Id] = doc;
        GetTargetPane().Children.Add(doc);
        doc.IsActive = true;
        doc.IsSelected = true;
    }

    private void OpenDirectSession(string url)
    {
        var view = new SessionView(_services, url);
        view.CloseRequested += () => CloseDocByView(view);

        var doc = new LayoutDocument
        {
            Title = view.SessionTitle,
            ContentId = $"direct:{Guid.NewGuid():N}",
            CanClose = true,
            Content = view,
        };

        doc.Closed += (_, _) =>
        {
            _directDocs.Remove(doc);
            view.Shutdown();
        };

        _directDocs.Add(doc);
        GetTargetPane().Children.Add(doc);
        doc.IsActive = true;
        doc.IsSelected = true;
    }

    private void CloseDocByView(SessionView view)
    {
        foreach (var doc in _tunnelDocs.Values.Concat(_directDocs))
        {
            if (ReferenceEquals(doc.Content, view))
            {
                doc.Close();
                return;
            }
        }
    }

    private void CloseTunnelDoc(string profileId)
    {
        if (_tunnelDocs.TryGetValue(profileId, out var doc))
        {
            doc.Close();
        }
    }

    private void StopAll_Click(object sender, RoutedEventArgs e)
    {
        _pendingProfiles.Clear();
        _services.Tunnels.StopAll();

        foreach (var doc in _tunnelDocs.Values.ToList())
        {
            doc.Close();
        }

        RefreshStatusBar();
    }

    // ------------------------------------------------------------------
    // Split / dock
    // ------------------------------------------------------------------

    private void SplitRight_Click(object sender, RoutedEventArgs e) =>
        SplitActiveDocument(System.Windows.Controls.Orientation.Horizontal);

    private void SplitDown_Click(object sender, RoutedEventArgs e) =>
        SplitActiveDocument(System.Windows.Controls.Orientation.Vertical);

    private void SplitActiveDocument(System.Windows.Controls.Orientation orientation)
    {
        if (DockManager.Layout.ActiveContent is not LayoutDocument active ||
            ReferenceEquals(active, _managementDoc))
        {
            return;
        }

        if (active.Parent is not LayoutDocumentPane oldPane || oldPane.Children.Count <= 1)
        {
            return;
        }

        if (oldPane.Parent is not ILayoutContainer parent)
        {
            return;
        }

        var newPane = new LayoutDocumentPane();
        var group = new LayoutDocumentPaneGroup { Orientation = orientation };

        parent.ReplaceChild(oldPane, group);
        group.Children.Add(oldPane);
        group.Children.Add(newPane);

        oldPane.RemoveChild(active);
        newPane.Children.Add(active);

        active.IsActive = true;
        active.IsSelected = true;
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
