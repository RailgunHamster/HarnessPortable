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
using DropZone = HarnessPortable.Windows.Services.TabDropZone;
using Size = System.Windows.Size;
using System.Globalization;

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
    private readonly LayoutPresetStore _layoutPresets;

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

    /// <summary>
    /// The drag overlay lives in its own transparent child window, not as a
    /// sibling Canvas in the main window: the panes host WebView2 (HwndHost),
    /// whose airspace always paints over overlapping WPF content, so an
    /// in-window overlay is invisible exactly where it matters — over the
    /// page. A borderless layered owned window draws above the HwndHost while
    /// WS_EX_TRANSPARENT keeps every mouse message flowing to the main window.
    /// </summary>
    private Window? _tabDragOverlayWindow;

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
        AppTheme.Track(this);

        // Flicker diagnostics: system-wide focus events + this window's
        // focus/geometry transitions land in %APPDATA%\HarnessPortable\flicker.log.
        FlickerLog.Start();
        // AppTheme resolved the palette before this log existed; record which one
        // the app actually opened with.
        FlickerLog.Log("theme", AppTheme.IsDark ? "dark tokens" : "light tokens");
        Activated += (_, _) => FlickerLog.Log("window", "Activated");
        Deactivated += (_, _) => FlickerLog.Log("window", "Deactivated");
        PreviewGotKeyboardFocus += (_, e) => FlickerLog.Log("wpf-focus", "got " + (e.NewFocus?.GetType().Name ?? "?"));
        PreviewLostKeyboardFocus += (_, e) => FlickerLog.Log("wpf-focus", "lost " + (e.OldFocus?.GetType().Name ?? "?"));
        SizeChanged += (_, _) => FlickerLog.Log("window", "SizeChanged");
        LocationChanged += (_, _) => FlickerLog.Log("window", "LocationChanged");

        InitializeTabDragOverlayWindow();
        Deactivated += (_, _) => EndTabDrag();
        LostMouseCapture += (_, _) => { if (_tabDragActive && Mouse.Captured != this) EndTabDrag(); };
        Closed += (_, _) => _tabDragOverlayWindow?.Close();

        InitializeDockLayout();
        DockManager.ActiveContentChanged += DockLayout_ActiveContentChanged;
        _services.Tunnels.StateChanged += OnTunnelStateChanged;
        _services.Updates.StateChanged += OnUpdateStateChangedForTaskbar;
        RefreshStatusBar();
        RefreshPresetBox(null);
        RestoreLastLayoutIfEnabled();
        InitializeWorkspaceUx();
        Loaded += (_, _) =>
        {
            // Resolve the two-slot selection rather than the retired single
            // URL, so startup follows whichever source is picked.
            var settings = _services.Settings.Load();
            _ = _services.Updates.CheckAsync(UpdateSelection.From(settings).Url());
        };
    }

    private void InitializeDockLayout()
    {
        // The workspace holds sessions only. The management UI is its own window
        // (see ManagementWindow), so it can never be hidden behind a tab and a
        // broken workspace can not lock the user out of it.
        var pane = new LayoutDocumentPane();
        var group = new LayoutDocumentPaneGroup(pane);
        DockManager.Layout = new LayoutRoot { RootPanel = new LayoutPanel(group) };
    }

    /// <summary>
    /// Opens the management window, or brings the open one forward. Independent
    /// of the workspace window on purpose: the tray entry and the toolbar button
    /// both reach it while the workspace is hidden, minimized or unusable.
    /// </summary>
    public void ShowManagementPanel()
    {
        ShowSettings();
    }

    private void OpenManagement_Click(object sender, RoutedEventArgs e) => ShowManagementPanel();

    /// <summary>
    /// Mirrors an in-flight update onto the taskbar button, which is where
    /// Windows users look for it. Harmless when the window is hidden (there is no
    /// button then); the tray covers that case.
    /// </summary>
    private void OnUpdateStateChangedForTaskbar()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(OnUpdateStateChangedForTaskbar);
            return;
        }

        var updates = _services.Updates;
        TaskbarItemInfo ??= new System.Windows.Shell.TaskbarItemInfo();
        TaskbarItemInfo.ProgressState = !updates.Busy
            ? System.Windows.Shell.TaskbarItemProgressState.None
            : updates.Progress > 0
                ? System.Windows.Shell.TaskbarItemProgressState.Normal
                : System.Windows.Shell.TaskbarItemProgressState.Indeterminate;
        TaskbarItemInfo.ProgressValue = updates.Progress / 100.0;
    }

    /// <summary>
    /// Keeps the keyboard chain alive across tab switches. Without this, the
    /// hidden webview that held Win32 focus drops it during the document swap
    /// and the newly activated one never claims it, so the next Ctrl+Tab (or
    /// any keystroke) is lost until the user clicks into the page.
    /// </summary>
    private void DockLayout_ActiveContentChanged(object? sender, EventArgs e)
    {
        if (_maximizedPane is { } pane && DockManager.Layout.ActiveContent is LayoutDocument active && active.Parent != pane) RestorePaneFocus();
        FlickerLog.Log("dock", "active=" + (DockManager.Layout.ActiveContent as LayoutDocument)?.Title);

        var activeView = (DockManager.Layout.ActiveContent as LayoutDocument)?.Content as SessionView;
        ConnectionLibraryView.SetActiveConnection(activeView?.ProfileId is { } profileId ? ConnectionLibrary.TunnelKey(profileId) :
            activeView?.DirectUrl is { } url ? ConnectionLibrary.DirectKey(url) : null);

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

    private void OnTunnelStateChanged(TunnelInfo info)    {
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
        WorkspaceEmptyState.Visibility = AllSessionDocs().Any() ? Visibility.Collapsed : Visibility.Visible;
        var active = _services.Tunnels.GetActiveStates().ToList();
        StopTunnelButton.Visibility = active.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        StatusText.Text = $"{active.Count} 个活动 SSH 连接 · {AllSessionDocs().Count()} 个网页标签 · 工作区自动保存";
        StatusText.ToolTip = active.Count == 0 ? "从连接库打开一个服务" : string.Join("\n", active.Select(s => $"{s.ProfileName} · {s.Status}"));
    }

    // ------------------------------------------------------------------
    // In-place tab dragging
    // ------------------------------------------------------------------

    private void DocumentTab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not LayoutDocumentTabItem tab ||
            tab.Model is not LayoutDocument doc ||
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
            if (_tabDragActive) EndTabDrag();
            else _tabDragCandidate = null;
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

    private void InitializeTabDragOverlayWindow()
    {
        // Detach the canvas from the main Grid first: WPF never auto-removes
        // a child from a Panel's Children, and a still-parented canvas makes
        // the overlay window's Show() throw "the specified child is already a
        // child of another Visual" (crash.log, 2.6.23).
        if (TabDragOverlay.Parent is System.Windows.Controls.Panel parentPanel)
        {
            parentPanel.Children.Remove(TabDragOverlay);
        }

        // All named element references keep working unchanged.
        _tabDragOverlayWindow = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
            Content = TabDragOverlay,
        };

        // Pure drawing surface: never take focus, never eat a mouse message.
        _tabDragOverlayWindow.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(_tabDragOverlayWindow).Handle;
            var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE,
                style | NativeMethods.WS_EX_LAYERED |
                NativeMethods.WS_EX_TRANSPARENT |
                NativeMethods.WS_EX_NOACTIVATE |
                NativeMethods.WS_EX_TOOLWINDOW);
        };
    }

    /// <summary>Covers the main window's client area exactly, so canvas
    /// coordinates equal screen-mapped window coordinates. Derived from the
    /// client rect in device pixels, not from Window.Left/Top/Width/Height:
    /// those hold restore bounds while maximized and exclude the frame, which
    /// left the cover half a window off and clipped the drop UI to one side.
    /// </summary>
    private void SyncTabDragOverlayBounds()
    {
        if (_tabDragOverlayWindow is null)
        {
            return;
        }

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? System.Windows.Media.Matrix.Identity;
        var topLeft = WpfPoint.Multiply(PointToScreen(new WpfPoint(0, 0)), transform);

        _tabDragOverlayWindow.Left = topLeft.X;
        _tabDragOverlayWindow.Top = topLeft.Y;
        _tabDragOverlayWindow.Width = ActualWidth;
        _tabDragOverlayWindow.Height = ActualHeight;
    }

    private void BeginTabDrag(WpfPoint point)
    {
        RestorePaneFocus();
        _tabDragActive = true;
        SyncTabDragOverlayBounds();
        if (_tabDragOverlayWindow is not null && _tabDragOverlayWindow.Owner is null)
        {
            _tabDragOverlayWindow.Owner = this;
        }

        // Show BEFORE capturing: CaptureMouse synchronously re-dispatches a
        // mouse-move (MouseDevice.Synchronize), which reaches
        // UpdateTabDragOverlay while this call is still on the stack — the
        // canvas must already be connected to a PresentationSource by then.
        _tabDragOverlayWindow?.Show();
        TabDragOverlay.Visibility = Visibility.Visible;
        DragHintCard.Visibility = Visibility.Visible;
        CaptureMouse();
        UpdateTabDragOverlay(point);
    }

    private void EndTabDrag()
    {
        _tabDragActive = false;
        if (Mouse.Captured == this)
        {
            ReleaseMouseCapture();
        }

        _tabDragCandidate = null;
        _tabDragTargetPane = null;
        _tabDragTargetRect = Rect.Empty;
        _tabDragOverlayWindow?.Hide();
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

        SyncTabDragOverlayBounds();
        var overlayPoint = PointToOverlay(windowPoint);
        TabDragOverlay.UpdateLayout();
        var previousCard = DropZoneCard.Visibility == Visibility.Visible
            ? new Rect(Canvas.GetLeft(DropZoneCard), Canvas.GetTop(DropZoneCard), DropZoneCard.Width, DropZoneCard.Height) : Rect.Empty;
        // Keep the target stable while using a card that extends over a narrow pane's neighbour.
        var pane = DropZoneCard.Visibility == Visibility.Visible && previousCard.Contains(overlayPoint)
            && _tabDragTargetPane?.Root == DockManager.Layout ? _tabDragTargetPane : FindPaneAt(windowPoint);
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

        // Keep the cover glued on in case the window moved or resized mid-drag.
        SyncTabDragOverlayBounds();

        // Screen-based mapping: the overlay canvas now lives in another
        // window, so TranslatePoint has no common visual ancestor to walk.
        // Before the overlay window is shown the canvas has no
        // PresentationSource and PointFromScreen throws — fall back to
        // main-window coordinates (the cover is positioned exactly over it).
        WpfPoint paneTopLeft;
        if (PresentationSource.FromVisual(TabDragOverlay) is null)
        {
            paneTopLeft = paneControl.TranslatePoint(new WpfPoint(0, 0), this);
        }
        else
        {
            paneTopLeft = TabDragOverlay.PointFromScreen(paneControl.PointToScreen(new WpfPoint(0, 0)));
        }

        var targetRect = new Rect(paneTopLeft, paneControl.RenderSize);
        _tabDragTargetRect = targetRect;

        Canvas.SetLeft(DragTargetHighlight, targetRect.Left);
        Canvas.SetTop(DragTargetHighlight, targetRect.Top);
        DragTargetHighlight.Width = targetRect.Width;
        DragTargetHighlight.Height = targetRect.Height;
        DragTargetHighlight.Visibility = Visibility.Visible;

        var cardRect = TabDropGeometry.CardBounds(targetRect, new Size(TabDragOverlay.ActualWidth, TabDragOverlay.ActualHeight), new Size(238, 174));
        DropZoneCard.Width = cardRect.Width;
        DropZoneCard.Height = cardRect.Height;
        Canvas.SetLeft(DropZoneCard, cardRect.Left);
        Canvas.SetTop(DropZoneCard, cardRect.Top);
        DropZoneCard.Visibility = Visibility.Visible;
        TabDragOverlay.UpdateLayout();
        Rect ButtonRect(System.Windows.Controls.Button button) => button.TransformToAncestor(TabDragOverlay).TransformBounds(new Rect(button.RenderSize));
        _tabDragZone = TabDropGeometry.ZoneAt(targetRect, overlayPoint, new Dictionary<DropZone, Rect>
        {
            [DropZone.Up] = ButtonRect(DropUpButton), [DropZone.Down] = ButtonRect(DropDownButton),
            [DropZone.Left] = ButtonRect(DropLeftButton), [DropZone.Right] = ButtonRect(DropRightButton),
            [DropZone.Center] = ButtonRect(DropCenterButton),
        });
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
        DragHintCard.MaxWidth = Math.Max(0, TabDragOverlay.ActualWidth - 16);
        DragHintCard.Measure(new Size(DragHintCard.MaxWidth, double.PositiveInfinity));
        var width = DragHintCard.DesiredSize.Width;
        Canvas.SetLeft(DragHintCard, Math.Max(8, (TabDragOverlay.ActualWidth - width) / 2));
        Canvas.SetTop(DragHintCard, 8);
    }

    private void UpdateDropZoneVisual(DropZone zone)
    {
        var normal = (System.Windows.Media.Brush)FindResource("DshBgLayer2");
        var active = (System.Windows.Media.Brush)FindResource("DshAccentSoft");
        var center = (System.Windows.Media.Brush)FindResource("DshBrandFill");

        DropUpButton.Background = zone == DropZone.Up ? active : normal;
        DropLeftButton.Background = zone == DropZone.Left ? active : normal;
        DropRightButton.Background = zone == DropZone.Right ? active : normal;
        DropDownButton.Background = zone == DropZone.Down ? active : normal;
        DropCenterButton.Background = center;
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
        // Before the overlay window is shown its canvas is not connected to a
        // PresentationSource and PointFromScreen throws; the cover is placed
        // exactly over the main window, so its coordinates coincide.
        if (PresentationSource.FromVisual(window.TabDragOverlay) is null)
        {
            return point;
        }

        var screenPoint = window.PointToScreen(point);
        return window.TabDragOverlay.PointFromScreen(screenPoint);
    }

    private WpfPoint PointToOverlay(WpfPoint point) => PointToOverlay(this, point);

    private void ApplyTabDrop(LayoutDocument doc, LayoutDocumentPane targetPane, DropZone zone)
    {
        // Restructuring the layout mutates the pane collections that the live
        // LayoutDocumentPaneControl is bound to, and AvalonDock reacts to the
        // removal by trying to write into its ItemsPanel's Children directly —
        // which WPF rejects with "无法显式修改 Panel 的 Children 集合". A throw
        // there used to unwind the whole drop halfway through (see crash.log),
        // leaving the dock model and its visual tree disagreeing about where
        // the tab lives. Deferring past the input event removes that
        // re-entrancy, and the guards below make a partial failure recoverable
        // instead of leaving a wedged window behind.
        var zoneValue = zone;
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            () => RunTabDrop(doc, targetPane, zoneValue));
    }

    private void RunTabDrop(LayoutDocument doc, LayoutDocumentPane targetPane, DropZone zone)
    {
        if (doc.Root is null || targetPane.Root is null)
        {
            // The document was closed, or the pane dropped out of the layout,
            // while the drop was queued. Nothing left to move.
            return;
        }

        if (zone == DropZone.Center)
        {
            MoveDocumentToPane(doc, targetPane);
            return;
        }

        SplitDocumentByMoving(doc, targetPane, zone);
    }

    private void MoveDocumentToPane(LayoutDocument doc, LayoutDocumentPane targetPane)
    {
        if (doc.Parent is not LayoutDocumentPane sourcePane)
        {
            return;
        }

        if (ReferenceEquals(sourcePane, targetPane))
        {
            doc.IsActive = true;
            doc.IsSelected = true;
            return;
        }

        Safely(
            () =>
            {
                sourcePane.Children.Remove(doc);
                targetPane.Children.Add(doc);
                doc.IsActive = true;
                doc.IsSelected = true;
                RemoveEmptyPane(sourcePane);
            },
            () => EnsureDocumentHasAPane(doc, sourcePane));
    }

    private void SplitDocumentByMoving(LayoutDocument doc, LayoutDocumentPane targetPane, DropZone zone)
    {
        if (doc.Parent is not LayoutDocumentPane sourcePane ||
            (ReferenceEquals(sourcePane, targetPane) && sourcePane.Children.Count <= 1) ||
            targetPane.Parent is not ILayoutContainer parent)
        {
            return;
        }

        Safely(
            () =>
            {
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
            },
            () => EnsureDocumentHasAPane(doc, sourcePane));
    }

    /// <summary>
    /// Runs one layout restructuring. AvalonDock updates its visual tree from
    /// the same collection change, so a throw in there can leave both the
    /// model and the view half-changed; <paramref name="recover"/> then has to
    /// put the layout back into a state the user can keep working in.
    /// </summary>
    private void Safely(Action operation, Action recover)
    {
        try
        {
            operation();
        }
        catch (Exception ex)
        {
            FlickerLog.Log("dock", "tab move failed: " + ex.GetType().Name + ": " + ex.Message);

            try
            {
                recover();
            }
            catch (Exception nested)
            {
                FlickerLog.Log("dock", "tab move recovery failed: " + nested.Message);
            }
        }
    }

    /// <summary>
    /// Last resort for a half-applied move: the document must never be left
    /// without a pane, because that is the state where the tab vanishes from
    /// the UI while still holding a live WebView2.
    /// </summary>
    private void EnsureDocumentHasAPane(LayoutDocument doc, LayoutDocumentPane preferred)
    {
        if (doc.Parent is LayoutDocumentPane)
        {
            return;
        }

        if (preferred.Root is not null)
        {
            preferred.Children.Add(doc);
            doc.IsActive = true;
            doc.IsSelected = true;
            return;
        }

        GetTargetPane().Children.Add(doc);
        doc.IsActive = true;
        doc.IsSelected = true;
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

    private void CollapseEmptyContainer(ILayoutContainer container)
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

        // Fall back to whichever pane is still in the tree, so a new tab never
        // lands somewhere invisible.
        if (DockManager.Layout.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault() is { } pane)
        {
            return pane;
        }

        pane = new LayoutDocumentPane();
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

        _connectionLibrary?.MarkOpened(ConnectionLibrary.TunnelKey(profile.Id));

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
        // The connect click happened in the management window: surf the
        // workspace so the newly opened tab is not missed behind it.
        BringToFront();

        if (_tunnelDocsByProfile.TryGetValue(profile.Id, out var docs) && docs.Count > 0)
        {
            var doc = docs[0];
            doc.IsActive = true;
            doc.IsSelected = true;
            return;
        }

        CreateTunnelDoc(profile, localPort, GetTargetPane(), activate: true);
    }

    private SessionView CreateTunnelView(TunnelProfile profile, int localPort, LayoutWebState? webState = null)
    {
        var view = new SessionView(_services, profile, localPort) { InitialWebState = webState };
        WireSessionView(view);
        return view;
    }

    private SessionView CreateDirectView(string url, LayoutWebState? webState = null)
    {
        var view = new SessionView(_services, url) { InitialWebState = webState };
        var preferredName = _services.Settings.Load().Connections.GetValueOrDefault(ConnectionLibrary.DirectKey(url))?.Name;
        if (!string.IsNullOrWhiteSpace(preferredName)) view.SetCustomLabel(preferredName);
        WireSessionView(view);
        return view;
    }

    private void WireSessionView(SessionView view)
    {
        view.WorkspaceStateChanged += ScheduleWorkspaceSnapshot;
        view.ReconnectRequested += () =>
        {
            if (view.ProfileId is not { } id || _services.Profiles.FindTunnel(id) is not { } profile || !EnsurePassword(profile)) return;
            _services.Tunnels.Stop(id);
            _services.Tunnels.Start(id);
        };
        view.AppFullScreenActive = _isFullScreen;
        view.PaneMaximized = IsPaneMaximized;
        view.CloseRequested += () => CloseDocByView(view);
        view.FullScreenToggleRequested += ToggleFullScreen;
        view.EscapeRequested += () => { if (IsPaneMaximized) RestorePaneFocus(); else ExitFullScreen(); };
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
        bool activate,
        LayoutWebState? webState = null)
    {
        var suffix = NextTunnelSuffix(profile.Id);
        var view = CreateTunnelView(profile, localPort, webState);

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

        AttachDocumentTitle(doc, view);

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

    private LayoutDocument CreateDirectDoc(
        string url,
        string suffix,
        LayoutDocumentPane targetPane,
        bool activate,
        LayoutWebState? webState = null)
    {
        var view = CreateDirectView(url, webState);

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
        AttachDocumentTitle(doc, view);

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
        BringToFront();
        _connectionLibrary?.MarkOpened(ConnectionLibrary.DirectKey(url));
        var existing = _directDocs.FirstOrDefault(d => (d.Content as SessionView)?.DirectUrl == url);
        if (existing is not null)
        {
            existing.IsActive = true; existing.IsSelected = true; return;
        }
        CreateDirectDoc(url, NextDirectSuffix(url), GetTargetPane(), activate: true);
    }

    private void AttachDocumentTitle(LayoutDocument doc, SessionView view)
    {
        view.AutomationTabId = doc.ContentId;
        view.TitleChanged += title =>
        {
            if (ReferenceEquals(doc.Content, view))
                doc.Title = title + (_docSuffixes.TryGetValue(doc, out var suffix) ? suffix : "");
        };
    }

    /// <summary>
    /// Shows the workspace window and raises it above the management window
    /// (and anything else), mirroring the tray reopen path in App.xaml.cs.
    /// </summary>
    private void BringToFront()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
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
        RefreshStatusBar();
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
        if (current is null || current.Content is not SessionView currentView)
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

            var item = Automation.AutomationControls.Menu("workspace.switch.tunnel."+profile.Id, $"隧道：{profile.DisplayName}", new LayoutTabRef { Kind="tunnel",ProfileId=profile.Id });
            item.Click += SwitchTab_Click;
            SwitchToMenu.Items.Add(item);
        }

        foreach (var url in _services.Profiles.LoadDirects())
        {
            if (!currentView.IsTunnel && currentView.DirectUrl == url)
            {
                continue;
            }

            var item = Automation.AutomationControls.Menu("workspace.switch.direct."+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url))), $"直连：{ProfileStore.HostOf(url)}", new LayoutTabRef { Kind="direct",Url=url });
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
        if (doc is null || doc.Content is not SessionView view)
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
            RenameDocument(doc, dialog.LayoutName);
        }
    }

    private void RenameDocument(LayoutDocument doc, string label)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new Automation.AutomationFault("invalid_arguments", "Tab label must not be empty.");
        _docSuffixes[doc] = "";
        ((SessionView)doc.Content).SetCustomLabel(label.Trim());
    }

    private void DuplicateTab_Click(object sender, RoutedEventArgs e)
    {
        var doc = GetContextDocumentFromMenuItem(sender);
        if (doc is null || doc.Content is not SessionView view)
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
        if (doc is null || doc.Content is not SessionView view)
        {
            return;
        }

        SplitDocument(doc, view, direction);
    }

    private void SplitDocument(LayoutDocument doc, SessionView view, SplitDirection direction)
    {
        RestorePaneFocus();
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
        if (doc.Content is not SessionView oldView)
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

        AttachDocumentTitle(doc, newView);

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

        SaveLayoutPreset(dialog.LayoutName);
    }

    private void SaveLayoutPreset(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new Automation.AutomationFault("invalid_arguments", "Layout name must not be empty.");
        var layout = new WorkspaceLayout
        {
            Name = name.Trim(),
            SavedAt = DateTime.UtcNow,
            Root = CaptureLayoutNode(DockManager.Layout.RootPanel) ?? new LayoutNode { Kind = "pane" },
        };

        _layoutPresets.Upsert(layout);
        var settings = _services.Settings.Load();
        settings.LastLayoutName = layout.Name;
        _services.Settings.Save(settings);
        RefreshPresetBox(layout.Name);
    }

    private void DeleteLayout_Click(object sender, RoutedEventArgs e)
    {
        if (LayoutPresetBox.SelectedItem is not WorkspaceLayout selected)
        {
            return;
        }

        DeleteLayoutPreset(selected);
    }

    private void DeleteLayoutPreset(WorkspaceLayout selected)
    {

        var confirm = HarnessPortable.Windows.Automation.AutomationDialogs.Show(
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
        var settings = _services.Settings.Load();
        if (string.Equals(settings.LastLayoutName, selected.Name, StringComparison.OrdinalIgnoreCase))
        {
            settings.LastLayoutName = "";
            _services.Settings.Save(settings);
        }
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
        if (!settings.RestoreLastLayoutOnStartup)
        {
            return;
        }

        var snapshot = _workspaceSnapshots.Load();
        var preset = snapshot ?? _layoutPresets.Load().FirstOrDefault(p =>
            string.Equals(p.Name, settings.LastLayoutName, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            return;
        }

        _suppressPresetSelection = true;
        LayoutPresetBox.SelectedItem = preset;
        _suppressPresetSelection = false;

        _connectDuringRestore = settings.ReconnectRestoredWorkspace;
        try { ApplyLayoutPreset(preset); }
        finally { _connectDuringRestore = true; }
    }

    private void ApplyLayoutPreset(WorkspaceLayout layout)
    {
        RestorePaneFocus();
        _restoredSelectedDocuments.Clear(); _restoredActiveDocument = null;
        _restoreConnectionReadiness.Clear();
        var restoredConfig = _services.Profiles.Load();
        _restoreProfiles = restoredConfig.Tunnels.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        _restoreDirects = restoredConfig.Directs.ToHashSet(StringComparer.Ordinal);
        _pendingProfiles.Clear();
        CloseAllSessionDocs();

        var body = DeserializeLayoutNode(layout.Root);
        var rootPanel = body switch
        {
            // A preset saved before the management UI became a window may hold
            // nothing but its tab: keep an empty pane so session tabs still have
            // somewhere to open.
            null => new LayoutPanel(new LayoutDocumentPane()),
            LayoutPanel panel => panel,
            _ => new LayoutPanel(body),
        };

        DockManager.Layout = new LayoutRoot { RootPanel = rootPanel };

        var appSettings = _services.Settings.Load();
        if (layout.Name != WorkspaceSnapshotName) appSettings.LastLayoutName = layout.Name;
        _services.Settings.Save(appSettings);

        foreach (var doc in _restoredSelectedDocuments) doc.IsSelected = true;
        var firstSession = _restoredActiveDocument ?? AllSessionDocs().FirstOrDefault();
        if (firstSession is not null)
        {
            firstSession.IsActive = true;
            firstSession.IsSelected = true;
        }

        RefreshStatusBar();
        _restoredSelectedDocuments.Clear(); _restoredActiveDocument = null;
        _restoreProfiles = null; _restoreDirects = null; _restoreConnectionReadiness.Clear();
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
        return $"{length.Value.ToString(CultureInfo.InvariantCulture)}|{length.GridUnitType}";
    }

    private static GridLength? DeserializeGridLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split('|');
        if (parts.Length != 2 ||
            !double.TryParse(parts[0].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var size) ||
            !Enum.TryParse<GridUnitType>(parts[1], out var unit) || !Enum.IsDefined(unit))
        {
            return null;
        }

        if (!double.IsFinite(size) || size <= 0)
        {
            return null;
        }

        return new GridLength(size, unit);
    }

    private void CaptureDockSizes(ILayoutElement element, LayoutNode node)
    {
        if (element is LayoutPanel or LayoutDocumentPaneGroup or LayoutDocumentPane)
        {
            node.DockWidth = SerializeGridLength(_paneFocus.SavedWidth(element));
            node.DockHeight = SerializeGridLength(_paneFocus.SavedHeight(element));
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
        return doc.Content is SessionView view
            ? view.IsTunnel && view.ProfileId is { } profileId
                ? new LayoutTabRef
                {
                    Kind = "tunnel",
                    ProfileId = profileId,
                    Label = view.CustomLabel,
                    Web = view.CurrentUrl is null ? view.InitialWebState : ViewStateUrls.Extract(view.CurrentUrl),
                    Selected = doc.IsSelected, Active = doc.IsActive,
                }
                : !view.IsTunnel && view.DirectUrl is { } url
                    ? new LayoutTabRef
                    {
                        Kind = "direct",
                        Url = url,
                        Label = view.CustomLabel,
                        Web = view.CurrentUrl is null ? view.InitialWebState : ViewStateUrls.Extract(view.CurrentUrl),
                        Selected = doc.IsSelected, Active = doc.IsActive,
                    }
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
            // Presets saved before the management UI became its own window still
            // carry its tab; there is nothing to restore for it now.
            return;
        }

        if (tab.Kind == "tunnel")
        {
            var profile = tab.ProfileId is null ? null : _restoreProfiles?.GetValueOrDefault(tab.ProfileId);
            if (profile is null)
            {
                return;
            }

            var state = _services.Tunnels.GetState(profile.Id);
            var reconnect = CanConnectRestored(profile);
            var port = state.LocalPort > 0 ? state.LocalPort : profile.LocalPort;
            var doc = CreateTunnelDoc(profile, port, pane, activate: false, webState: tab.Web);
            if (tab.Selected) _restoredSelectedDocuments.Add(doc);
            if (tab.Active) _restoredActiveDocument = doc;
            if (tab.Label is { } label && doc.Content is SessionView tunnelView)
            {
                _docSuffixes[doc] = "";
                tunnelView.SetCustomLabel(label);
            }

            if (reconnect && state.Status is not (TunnelStatus.Connected or TunnelStatus.Connecting or TunnelStatus.Retrying))
            {
                _services.Tunnels.Start(profile.Id);
            }
        }
        else if (tab.Kind == "direct" && tab.Url is { } url && _restoreDirects?.Contains(url) == true)
        {
            var doc = CreateDirectDoc(url, NextDirectSuffix(url), pane, activate: false, webState: tab.Web);
            if (tab.Selected) _restoredSelectedDocuments.Add(doc);
            if (tab.Active) _restoredActiveDocument = doc;
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
        else if (IsPaneMaximized && e.Key == Key.Escape)
        {
            RestorePaneFocus();
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
        ApplySidebarVisibility();
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
        SaveWorkspaceSnapshot();
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
            HarnessPortable.Windows.Automation.AutomationDialogs.Show(
                "Harness Portable 仍在托盘运行，已连接的隧道不会中断。\n双击托盘图标可重新打开窗口；右键图标可打开「管理 / 设置」、检查更新或退出。",
                "Harness Portable",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
