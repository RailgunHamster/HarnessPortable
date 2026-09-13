using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AvalonDock.Controls;
using AvalonDock.Layout;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
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

    private enum DropZone
    {
        Center,
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

    // AvalonDock floating windows are disabled because WebView2 is an HwndHost.
    // These fields implement an in-place tab drag so docking never reparents it.
    private LayoutDocument? _tabDragCandidate;
    private WpfPoint _tabDragStartPoint;
    private bool _tabDragActive;
    private LayoutDocumentPane? _tabDragTargetPane;
    private Rect _tabDragTargetRect;
    private DropZone _tabDragZone;

    private bool _trayHintShown;
    private bool _isFullScreen;
    private WindowStyle _normalWindowStyle;
    private WindowState _preFullScreenState;
    private ResizeMode _normalResizeMode;
    private bool _normalTopmost;
    private double _normalLeft;
    private double _normalTop;
    private double _normalWidth;
    private double _normalHeight;

    public MainWindow(AppServices services)
    {
        _services = services;
        _layoutPresets = new LayoutPresetStore();
        InitializeComponent();

        // Flicker diagnostics: system-wide focus events + this window's
        // focus/geometry transitions land in %APPDATA%\HarnessPortable\flicker.log.
        FlickerLog.Start();
        Activated += (_, _) => FlickerLog.Log("window", "Activated");
        Deactivated += (_, _) => FlickerLog.Log("window", "Deactivated");
        PreviewGotKeyboardFocus += (_, e) => FlickerLog.Log("wpf-focus", "got " + (e.NewFocus?.GetType().Name ?? "?"));
        PreviewLostKeyboardFocus += (_, e) => FlickerLog.Log("wpf-focus", "lost " + (e.OldFocus?.GetType().Name ?? "?"));
        SizeChanged += (_, _) => FlickerLog.Log("window", "SizeChanged");
        LocationChanged += (_, _) => FlickerLog.Log("window", "LocationChanged");

        _managementView = new ManagementView(_services);
        _managementView.TunnelConnectRequested += ConnectTunnel;
        _managementView.TunnelStopRequested += CloseTunnelDocs;
        _managementView.DirectOpenRequested += OpenDirectSession;

        InitializeDockLayout();
        DockManager.ActiveContentChanged += DockLayout_ActiveContentChanged;
        _services.Tunnels.StateChanged += OnTunnelStateChanged;
        RefreshStatusBar();
        RefreshPresetBox(null);
        RestoreLastLayoutIfEnabled();
    }

    private void InitializeDockLayout()
    {
        _managementDoc = CreateManagementDoc();
        var pane = new LayoutDocumentPane(_managementDoc);
        var group = new LayoutDocumentPaneGroup(pane);
        DockManager.Layout = new LayoutRoot { RootPanel = new LayoutPanel(group) };
        _managementDoc.IsActive = true;
    }

    /// <summary>
    /// Keeps the keyboard chain alive across tab switches. Without this, the
    /// hidden webview that held Win32 focus drops it during the document swap
    /// and the newly activated one never claims it, so the next Ctrl+Tab (or
    /// any keystroke) is lost until the user clicks into the page.
    /// </summary>
    private void DockLayout_ActiveContentChanged(object? sender, EventArgs e)
    {
        FlickerLog.Log("dock", "active=" + (DockManager.Layout.ActiveContent as LayoutDocument)?.Title);

        // Don't steal focus from another application (e.g. while a layout is
        // being restored in the background on startup).
        if (!IsActive)
        {
            return;
        }

        if (DockManager.Layout.ActiveContent is not LayoutDocument { Content: SessionView view })
        {
            return;
        }

        view.FocusWebView();
    }

    private LayoutDocument CreateManagementDoc() => new()
    {
        Title = "管理",
        ContentId = "management",
        CanClose = false,
        CanFloat = false,
        CanMove = false,
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
    // In-place tab dragging
    // ------------------------------------------------------------------

    private void DocumentTab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not LayoutDocumentTabItem tab ||
            tab.Model is not LayoutDocument doc ||
            ReferenceEquals(doc, _managementDoc) ||
            !doc.CanMove)
        {
            return;
        }

        _tabDragCandidate = doc;
        _tabDragStartPoint = e.GetPosition(this);
        _tabDragActive = false;
    }

    private void Window_PreviewMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (_tabDragCandidate is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!_tabDragActive)
        {
            var movedEnough = Math.Abs(point.X - _tabDragStartPoint.X) >= SystemParameters.MinimumHorizontalDragDistance ||
                              Math.Abs(point.Y - _tabDragStartPoint.Y) >= SystemParameters.MinimumVerticalDragDistance;
            if (!movedEnough)
            {
                return;
            }

            // Let AvalonDock keep its native same-tab ordering. Once the
            // pointer leaves the source tab strip, take over with an in-place
            // drag that never creates a floating WebView2 window.
            var sourcePane = _tabDragCandidate.Parent as LayoutDocumentPane;
            var targetPane = FindPaneAt(point);
            if (ReferenceEquals(sourcePane, targetPane) && IsOverDocumentTabStrip(point))
            {
                return;
            }

            BeginTabDrag(point);
        }

        UpdateTabDragOverlay(point);
        e.Handled = true;
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_tabDragActive)
        {
            _tabDragCandidate = null;
            return;
        }

        var point = e.GetPosition(this);
        UpdateTabDragOverlay(point);

        var source = _tabDragCandidate;
        var target = _tabDragTargetPane;
        var zone = _tabDragZone;
        EndTabDrag();

        if (source is not null && target is not null)
        {
            ApplyTabDrop(source, target, zone);
        }

        e.Handled = true;
    }

    private void BeginTabDrag(WpfPoint point)
    {
        _tabDragActive = true;
        CaptureMouse();
        TabDragOverlay.Visibility = Visibility.Visible;
        DragHintCard.Visibility = Visibility.Visible;
        UpdateTabDragOverlay(point);
    }

    private void EndTabDrag()
    {
        if (Mouse.Captured == this)
        {
            ReleaseMouseCapture();
        }

        _tabDragCandidate = null;
        _tabDragActive = false;
        _tabDragTargetPane = null;
        _tabDragTargetRect = Rect.Empty;
        TabDragOverlay.Visibility = Visibility.Collapsed;
        DragTargetHighlight.Visibility = Visibility.Collapsed;
        DropZoneCard.Visibility = Visibility.Collapsed;
    }

    private void UpdateTabDragOverlay(WpfPoint windowPoint)
    {
        if (!_tabDragActive)
        {
            return;
        }

        var overlayPoint = PointToOverlay(windowPoint);
        var pane = FindPaneAt(windowPoint);
        _tabDragTargetPane = pane;

        if (pane is null || FindPaneControl(pane) is not { } paneControl)
        {
            _tabDragTargetRect = Rect.Empty;
            DragTargetHighlight.Visibility = Visibility.Collapsed;
            DropZoneCard.Visibility = Visibility.Collapsed;
            DragHintText.Text = "拖到其他标签区域后释放";
            UpdateDragHintPosition();
            return;
        }

        var topLeft = paneControl.TranslatePoint(new WpfPoint(0, 0), TabDragOverlay);
        var targetRect = new Rect(topLeft, paneControl.RenderSize);
        _tabDragTargetRect = targetRect;

        Canvas.SetLeft(DragTargetHighlight, targetRect.Left);
        Canvas.SetTop(DragTargetHighlight, targetRect.Top);
        DragTargetHighlight.Width = targetRect.Width;
        DragTargetHighlight.Height = targetRect.Height;
        DragTargetHighlight.Visibility = Visibility.Visible;

        var cardWidth = DropZoneCard.Width;
        var cardHeight = DropZoneCard.Height;
        var cardLeft = Math.Max(targetRect.Left + 8,
            Math.Min(targetRect.Right - cardWidth - 8, targetRect.Left + (targetRect.Width - cardWidth) / 2));
        var cardTop = Math.Max(targetRect.Top + 8,
            Math.Min(targetRect.Bottom - cardHeight - 8, targetRect.Top + (targetRect.Height - cardHeight) / 2));
        Canvas.SetLeft(DropZoneCard, cardLeft);
        Canvas.SetTop(DropZoneCard, cardTop);
        DropZoneCard.Visibility = Visibility.Visible;

        var cardRect = new Rect(cardLeft, cardTop, cardWidth, cardHeight);
        _tabDragZone = DetermineDropZone(targetRect, cardRect, overlayPoint);
        UpdateDropZoneVisual(_tabDragZone);
        DragHintText.Text = _tabDragZone switch
        {
            DropZone.Center => "放入标签区：合并为同一组",
            DropZone.Left => "向左分屏",
            DropZone.Right => "向右分屏",
            DropZone.Up => "向上分屏",
            DropZone.Down => "向下分屏",
            _ => "拖到目标区域后释放",
        };
        UpdateDragHintPosition();
    }

    private void UpdateDragHintPosition()
    {
        var width = DragHintCard.ActualWidth > 0 ? DragHintCard.ActualWidth : 220;
        Canvas.SetLeft(DragHintCard, Math.Max(8, (TabDragOverlay.ActualWidth - width) / 2));
        Canvas.SetTop(DragHintCard, 8);
    }

    private void UpdateDropZoneVisual(DropZone zone)
    {
        var normal = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xFF, 0xFF, 0xFF));
        var active = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xD9, 0xE2, 0xFF));
        var center = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x4D, 0x6B, 0xFE));

        DropUpButton.Background = zone == DropZone.Up ? active : normal;
        DropLeftButton.Background = zone == DropZone.Left ? active : normal;
        DropRightButton.Background = zone == DropZone.Right ? active : normal;
        DropDownButton.Background = zone == DropZone.Down ? active : normal;
        DropCenterButton.Background = zone == DropZone.Center ? center : normal;
    }

    private static DropZone DetermineDropZone(Rect targetRect, Rect cardRect, WpfPoint point)
    {
        if (cardRect.Contains(point))
        {
            var column = (point.X - cardRect.Left) / cardRect.Width;
            var row = (point.Y - cardRect.Top) / cardRect.Height;
            if (column is >= 1.0 / 3.0 and <= 2.0 / 3.0 &&
                row is >= 1.0 / 3.0 and <= 2.0 / 3.0)
            {
                return DropZone.Center;
            }

            if (row < 1.0 / 3.0) return DropZone.Up;
            if (row > 2.0 / 3.0) return DropZone.Down;
            if (column < 1.0 / 3.0) return DropZone.Left;
            return DropZone.Right;
        }

        var edge = Math.Max(48, Math.Min(targetRect.Width, targetRect.Height) * 0.22);
        if (point.X <= targetRect.Left + edge) return DropZone.Left;
        if (point.X >= targetRect.Right - edge) return DropZone.Right;
        if (point.Y <= targetRect.Top + edge) return DropZone.Up;
        if (point.Y >= targetRect.Bottom - edge) return DropZone.Down;
        return DropZone.Center;
    }

    private LayoutDocumentPane? FindPaneAt(WpfPoint windowPoint)
    {
        var screenPoint = PointToScreen(windowPoint);
        var dockPoint = DockManager.PointFromScreen(screenPoint);
        var hit = DockManager.InputHitTest(dockPoint) as DependencyObject;
        if (FindVisualParent<LayoutDocumentPaneControl>(hit) is { Model: LayoutDocumentPane hitPane })
        {
            return hitPane;
        }

        foreach (var control in FindVisualChildren<LayoutDocumentPaneControl>(DockManager))
        {
            var topLeft = control.TranslatePoint(new WpfPoint(0, 0), DockManager);
            if (new Rect(topLeft, control.RenderSize).Contains(dockPoint) &&
                control.Model is LayoutDocumentPane pane)
            {
                return pane;
            }
        }

        return null;
    }

    private LayoutDocumentPaneControl? FindPaneControl(LayoutDocumentPane pane)
    {
        return FindVisualChildren<LayoutDocumentPaneControl>(DockManager)
            .FirstOrDefault(control => ReferenceEquals(control.Model, pane));
    }

    private bool IsOverDocumentTabStrip(WpfPoint windowPoint)
    {
        var screenPoint = PointToScreen(windowPoint);
        var dockPoint = DockManager.PointFromScreen(screenPoint);
        var hit = DockManager.InputHitTest(dockPoint) as DependencyObject;
        return FindVisualParent<DocumentPaneTabPanel>(hit) is not null;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        var current = child;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is null)
        {
            yield break;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static WpfPoint PointToOverlay(MainWindow window, WpfPoint point)
    {
        var screenPoint = window.PointToScreen(point);
        return window.TabDragOverlay.PointFromScreen(screenPoint);
    }

    private WpfPoint PointToOverlay(WpfPoint point) => PointToOverlay(this, point);

    private void ApplyTabDrop(LayoutDocument doc, LayoutDocumentPane targetPane, DropZone zone)
    {
        if (zone == DropZone.Center)
        {
            MoveDocumentToPane(doc, targetPane);
            return;
        }

        SplitDocumentByMoving(doc, targetPane, zone);
    }

    private void MoveDocumentToPane(LayoutDocument doc, LayoutDocumentPane targetPane)
    {
        if (doc.Parent is not LayoutDocumentPane sourcePane || ReferenceEquals(sourcePane, targetPane))
        {
            doc.IsActive = true;
            doc.IsSelected = true;
            return;
        }

        sourcePane.Children.Remove(doc);
        targetPane.Children.Add(doc);
        doc.IsActive = true;
        doc.IsSelected = true;
        RemoveEmptyPane(sourcePane);
    }

    private void SplitDocumentByMoving(LayoutDocument doc, LayoutDocumentPane targetPane, DropZone zone)
    {
        if (doc.Parent is not LayoutDocumentPane sourcePane ||
            (ReferenceEquals(sourcePane, targetPane) && sourcePane.Children.Count <= 1) ||
            targetPane.Parent is not ILayoutContainer parent)
        {
            return;
        }

        sourcePane.Children.Remove(doc);

        var newPane = new LayoutDocumentPane();
        var group = new LayoutDocumentPaneGroup
        {
            Orientation = zone is DropZone.Left or DropZone.Right
                ? System.Windows.Controls.Orientation.Horizontal
                : System.Windows.Controls.Orientation.Vertical,
        };

        parent.ReplaceChild(targetPane, group);
        if (zone is DropZone.Left or DropZone.Up)
        {
            group.Children.Add(newPane);
            group.Children.Add(targetPane);
        }
        else
        {
            group.Children.Add(targetPane);
            group.Children.Add(newPane);
        }

        newPane.Children.Add(doc);
        doc.IsActive = true;
        doc.IsSelected = true;

        if (!ReferenceEquals(sourcePane, targetPane))
        {
            RemoveEmptyPane(sourcePane);
        }
    }

    private void RemoveEmptyPane(LayoutDocumentPane pane)
    {
        if (pane.Children.Count != 0 || pane.Parent is not ILayoutContainer parent)
        {
            return;
        }

        parent.RemoveChild(pane);
        CollapseEmptyContainer(parent);
    }

    private static void CollapseEmptyContainer(ILayoutContainer container)
    {
        if (container.ChildrenCount == 0 &&
            container is ILayoutElement emptyElement &&
            emptyElement.Parent is ILayoutContainer grandParent)
        {
            grandParent.RemoveChild(emptyElement);
            CollapseEmptyContainer(grandParent);
            return;
        }

        if (container.ChildrenCount != 1 ||
            container is not ILayoutElement element ||
            element.Parent is not ILayoutContainer parent)
        {
            return;
        }

        parent.ReplaceChild(element, container.Children.First());
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
        if (_services.Secrets.HasPassword(profile.Id) ||
            SshIdentity.HasUsableKey(profile.IdentityFile))
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
            CanFloat = false,
            CanMove = true,
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
            CanFloat = false,
            CanMove = true,
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
        LayoutDocument? created = null;

        if (source.IsTunnel && source.ProfileId is { } profileId)
        {
            var profile = _services.Profiles.FindTunnel(profileId);
            if (profile is not null)
            {
                created = CreateTunnelDoc(profile, source.LocalPort, targetPane, activate);
            }
        }
        else if (!source.IsTunnel && source.DirectUrl is { } url)
        {
            created = CreateDirectDoc(url, NextDirectSuffix(url), targetPane, activate);
        }

        if (created?.Content is SessionView duplicate && source.CustomLabel is { } label)
        {
            duplicate.SetCustomLabel(label);
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

    private void RenameTab_Click(object sender, RoutedEventArgs e)
    {
        var doc = GetContextDocumentFromMenuItem(sender) ?? _contextDoc;
        if (doc is null || ReferenceEquals(doc, _managementDoc) || doc.Content is not SessionView view)
        {
            return;
        }

        var dialog = new LayoutNameWindow(view.DisplayLabel)
        {
            Owner = this,
            Title = "重命名标签",
        };

        if (dialog.ShowDialog() == true)
        {
            // An explicit user rename replaces any duplicate suffix like " (2)".
            _docSuffixes[doc] = "";
            view.SetCustomLabel(dialog.LayoutName);
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
            if (target.Label is { } label)
            {
                _docSuffixes[doc] = "";
                newView.SetCustomLabel(label);
            }

            if (state.Status is not (TunnelStatus.Connected or TunnelStatus.Connecting or TunnelStatus.Retrying))
            {
                _services.Tunnels.Start(profile.Id);
            }
        }
        else if (target.Kind == "direct" && target.Url is { } url)
        {
            var newView = CreateDirectView(url);
            ReplaceDocumentContent(doc, oldView, newView, newTunnelProfileId: null);
            if (target.Label is { } label)
            {
                _docSuffixes[doc] = "";
                newView.SetCustomLabel(label);
            }
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

    private void RestoreLastLayoutIfEnabled()
    {
        var settings = _services.Settings.Load();
        if (!settings.RestoreLastLayoutOnStartup || string.IsNullOrWhiteSpace(settings.LastLayoutName))
        {
            return;
        }

        var preset = _layoutPresets.Load().FirstOrDefault(p =>
            string.Equals(p.Name, settings.LastLayoutName, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            return;
        }

        _suppressPresetSelection = true;
        LayoutPresetBox.SelectedItem = preset;
        _suppressPresetSelection = false;

        ApplyLayoutPreset(preset);
    }

    private void ApplyLayoutPreset(WorkspaceLayout layout)
    {
        _pendingProfiles.Clear();
        CloseAllSessionDocs();

        _managementDoc = CreateManagementDoc();

        var body = DeserializeLayoutNode(layout.Root);
        var rootPanel = body switch
        {
            null => new LayoutPanel(new LayoutDocumentPane(_managementDoc)),
            LayoutPanel panel => panel,
            _ => new LayoutPanel(body),
        };

        if (!ContainsManagement(layout.Root))
        {
            rootPanel.Children.Insert(0, new LayoutDocumentPane(_managementDoc));
        }

        DockManager.Layout = new LayoutRoot { RootPanel = rootPanel };

        var appSettings = _services.Settings.Load();
        appSettings.LastLayoutName = layout.Name;
        _services.Settings.Save(appSettings);

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

    private bool ContainsManagement(LayoutNode node)
    {
        if (node.Kind == "pane")
        {
            return node.Tabs.Any(t => t.Kind == "management");
        }

        return node.Children.Any(ContainsManagement);
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

    private static string? SerializeGridLength(GridLength length)
    {
        return $"{length.Value}|{length.GridUnitType}";
    }

    private static GridLength? DeserializeGridLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split('|');
        if (parts.Length != 2 ||
            !double.TryParse(parts[0], out var size) ||
            !Enum.TryParse<GridUnitType>(parts[1], out var unit))
        {
            return null;
        }

        if (size <= 0)
        {
            return null;
        }

        return new GridLength(size, unit);
    }

    private static void CaptureDockSizes(ILayoutElement element, LayoutNode node)
    {
        switch (element)
        {
            case LayoutPanel panel:
                node.DockWidth = SerializeGridLength(panel.DockWidth);
                node.DockHeight = SerializeGridLength(panel.DockHeight);
                break;
            case LayoutDocumentPaneGroup group:
                node.DockWidth = SerializeGridLength(group.DockWidth);
                node.DockHeight = SerializeGridLength(group.DockHeight);
                break;
            case LayoutDocumentPane pane:
                node.DockWidth = SerializeGridLength(pane.DockWidth);
                node.DockHeight = SerializeGridLength(pane.DockHeight);
                break;
        }
    }

    private static void ApplyDockSizes(LayoutNode node, ILayoutElement element)
    {
        var width = DeserializeGridLength(node.DockWidth);
        var height = DeserializeGridLength(node.DockHeight);

        void SetWidth(Action<GridLength> setter)
        {
            if (width is { } w)
            {
                setter(w);
            }
        }

        void SetHeight(Action<GridLength> setter)
        {
            if (height is { } h)
            {
                setter(h);
            }
        }

        switch (element)
        {
            case LayoutPanel panel:
                SetWidth(v => panel.DockWidth = v);
                SetHeight(v => panel.DockHeight = v);
                break;

            case LayoutDocumentPaneGroup group:
                SetWidth(v => group.DockWidth = v);
                SetHeight(v => group.DockHeight = v);
                break;

            case LayoutDocumentPane pane:
                SetWidth(v => pane.DockWidth = v);
                SetHeight(v => pane.DockHeight = v);
                break;
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

                if (tabs.Count == 0)
                {
                    return null;
                }

                var node = new LayoutNode { Kind = "pane", Tabs = tabs };
                CaptureDockSizes(pane, node);
                return node;
            }

            case LayoutPanel panel:
            {
                var children = panel.Children
                    .Select(CaptureLayoutNode)
                    .Where(n => n is not null)
                    .Select(n => n!)
                    .ToList();

                if (children.Count == 0)
                {
                    return null;
                }

                var node = new LayoutNode
                {
                    Kind = "split",
                    Orientation = panel.Orientation == System.Windows.Controls.Orientation.Horizontal
                        ? "horizontal"
                        : "vertical",
                    Children = children,
                };
                CaptureDockSizes(panel, node);
                return node;
            }

            case LayoutDocumentPaneGroup group:
            {
                var children = group.Children
                    .Select(CaptureLayoutNode)
                    .Where(n => n is not null)
                    .Select(n => n!)
                    .ToList();

                if (children.Count == 0)
                {
                    return null;
                }

                var node = new LayoutNode
                {
                    Kind = "split",
                    Orientation = group.Orientation == System.Windows.Controls.Orientation.Horizontal
                        ? "horizontal"
                        : "vertical",
                    Children = children,
                };
                CaptureDockSizes(group, node);
                return node;
            }

            default:
                return null;
        }
    }

    private LayoutTabRef? TabRefFromDoc(LayoutDocument doc)
    {
        if (ReferenceEquals(doc, _managementDoc))
        {
            return new LayoutTabRef { Kind = "management" };
        }

        return doc.Content is SessionView view
            ? view.IsTunnel && view.ProfileId is { } profileId
                ? new LayoutTabRef
                {
                    Kind = "tunnel",
                    ProfileId = profileId,
                    Label = view.CustomLabel,
                }
                : !view.IsTunnel && view.DirectUrl is { } url
                    ? new LayoutTabRef { Kind = "direct", Url = url, Label = view.CustomLabel }
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

            if (pane.Children.Count == 0)
            {
                return null;
            }

            ApplyDockSizes(node, pane);
            return pane;
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

        if (panel.Children.Count == 0)
        {
            return null;
        }

        ApplyDockSizes(node, panel);
        return panel;
    }

    private void CreateSessionForTab(LayoutTabRef tab, LayoutDocumentPane pane)
    {
        if (tab.Kind == "management")
        {
            pane.Children.Add(_managementDoc);
            return;
        }

        if (tab.Kind == "tunnel")
        {
            var profile = tab.ProfileId is null ? null : _services.Profiles.FindTunnel(tab.ProfileId);
            if (profile is null || !EnsurePassword(profile))
            {
                return;
            }

            var state = _services.Tunnels.GetState(profile.Id);
            var port = state.LocalPort > 0 ? state.LocalPort : profile.LocalPort;
            var doc = CreateTunnelDoc(profile, port, pane, activate: false);
            if (tab.Label is { } label && doc.Content is SessionView tunnelView)
            {
                _docSuffixes[doc] = "";
                tunnelView.SetCustomLabel(label);
            }

            if (state.Status is not (TunnelStatus.Connected or TunnelStatus.Connecting or TunnelStatus.Retrying))
            {
                _services.Tunnels.Start(profile.Id);
            }
        }
        else if (tab.Kind == "direct" && tab.Url is { } url)
        {
            var doc = CreateDirectDoc(url, NextDirectSuffix(url), pane, activate: false);
            if (tab.Label is { } label && doc.Content is SessionView directView)
            {
                _docSuffixes[doc] = "";
                directView.SetCustomLabel(label);
            }
        }
    }

    // ------------------------------------------------------------------
    // Full screen
    // ------------------------------------------------------------------

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_tabDragActive && e.Key == Key.Escape)
        {
            EndTabDrag();
            e.Handled = true;
        }
        else if (e.Key == Key.F11)
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
        _normalLeft = Left;
        _normalTop = Top;
        _normalWidth = Width;
        _normalHeight = Height;

        SetSessionFullScreenState(true);

        ChromeBar.Visibility = Visibility.Collapsed;
        ChromeStatus.Visibility = Visibility.Collapsed;

        // Cover the current monitor completely, including the taskbar.
        // Screen.Bounds is in physical pixels; WPF Left/Top/Width/Height are
        // DIPs, so convert through the composition target to avoid a window
        // that is physically larger than the monitor on scaled displays.
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var deviceToDip = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        WindowState = WindowState.Normal;

        if (deviceToDip.HasValue)
        {
            var matrix = deviceToDip.Value;
            var topLeft = matrix.Transform(new System.Windows.Point(screen.Bounds.Left, screen.Bounds.Top));
            var bottomRight = matrix.Transform(new System.Windows.Point(screen.Bounds.Right, screen.Bounds.Bottom));
            Left = topLeft.X;
            Top = topLeft.Y;
            Width = bottomRight.X - topLeft.X;
            Height = bottomRight.Y - topLeft.Y;
        }
        else
        {
            Left = screen.Bounds.Left;
            Top = screen.Bounds.Top;
            Width = screen.Bounds.Width;
            Height = screen.Bounds.Height;
        }
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
        WindowState = WindowState.Normal;
        Left = _normalLeft;
        Top = _normalTop;
        Width = _normalWidth;
        Height = _normalHeight;

        if (_preFullScreenState == WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
        else if (_preFullScreenState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
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

        if (_services.Settings.Load().CloseBehavior != "tray")
        {
            // Default: closing the main window exits the app and stops tunnels.
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
