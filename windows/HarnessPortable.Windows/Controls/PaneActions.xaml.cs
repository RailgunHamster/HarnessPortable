using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HarnessPortable.Windows.Services;
using MenuItem = System.Windows.Controls.MenuItem;

namespace HarnessPortable.Windows.Controls;

/// <summary>Commands belong to this pane's selected session, not the window's active pane.</summary>
public partial class PaneActions : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(SessionView), typeof(PaneActions), new PropertyMetadata(null, SessionChanged));
    public static readonly DependencyProperty PaneWidthProperty = DependencyProperty.Register(
        nameof(PaneWidth), typeof(double), typeof(PaneActions), new PropertyMetadata(0d, PaneWidthChanged));
    public SessionView? Session { get => (SessionView?)GetValue(SessionProperty); set => SetValue(SessionProperty, value); }
    public double PaneWidth { get => (double)GetValue(PaneWidthProperty); set => SetValue(PaneWidthProperty, value); }
    public bool IsCompact => PaneWidth < 320;
    private Window? _owner;
    private System.Windows.Controls.ContextMenu? _menu;

    public PaneActions()
    {
        InitializeComponent();
        UpdateButtons();
        Loaded += (_, _) => AttachOwner();
        Unloaded += (_, _) => { CloseMenu(); DetachOwner(); };
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseMenu(); };
    }

    private static void SessionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var actions = (PaneActions)sender;
        actions.CloseMenu();
        actions.UpdateButtons();
    }
    private static void PaneWidthChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var actions = (PaneActions)sender;
        actions.CloseMenu();
        actions.UpdateButtons();
    }
    private void UpdateButtons()
    {
        if (RefreshButton is null || MoreButton is null) return;
        RefreshButton.Visibility = IsCompact ? Visibility.Collapsed : Visibility.Visible;
        RefreshButton.IsEnabled = MoreButton.IsEnabled = Session is not null;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        CloseMenu();
        if (Session is { } session) await RefreshAsync(session);
    }
    private static async Task RefreshAsync(SessionView session)
    {
        try { await session.RefreshAsync(); }
        catch (Exception error) { FlickerLog.Log("session", "refresh failed: " + error.GetType().Name); }
    }

    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (_menu?.IsOpen == true) { CloseMenu(); return; }
        if (Session is not { } session || !IsLoaded || !IsVisible || _owner is null || !_owner.IsVisible || _owner.WindowState == WindowState.Minimized) return;
        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = MoreButton, Placement = PlacementMode.Bottom, StaysOpen = false };
        void Add(string label, Action action, bool danger = false)
        {
            var item = new MenuItem { Header = label };
            if (danger) item.SetResourceReference(ForegroundProperty, "DshDangerText");
            item.Click += (_, _) =>
            {
                CloseMenu();
                // Selection may change before a queued click is delivered.
                if (ReferenceEquals(Session, session) && IsLoaded && IsVisible && _owner?.IsVisible == true
                    && _owner.WindowState != WindowState.Minimized) action();
            };
            menu.Items.Add(item);
        }
        if (IsCompact) Add("刷新", async () => await RefreshAsync(session));
        Add("认证", session.RequestAuthentication);
        if (session.IsTunnel)
        {
            menu.Items.Add(new Separator());
            Add("重连", session.RequestReconnect);
            Add("断开", session.RequestDisconnect, danger: true);
        }
        _menu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_menu, menu)) _menu = null; };
        menu.IsOpen = true;
    }

    private void CloseMenu()
    {
        if (_menu is not { } menu) return;
        _menu = null;
        menu.IsOpen = false;
    }
    private void AttachOwner()
    {
        var owner = Window.GetWindow(this);
        if (ReferenceEquals(owner, _owner)) return;
        DetachOwner();
        _owner = owner;
        if (owner is null) return;
        owner.Deactivated += OwnerChanged;
        owner.StateChanged += OwnerChanged;
        owner.LocationChanged += OwnerChanged;
        owner.SizeChanged += OwnerSizeChanged;
        owner.IsVisibleChanged += OwnerVisibilityChanged;
        owner.Closing += OwnerClosing;
    }
    private void DetachOwner()
    {
        CloseMenu();
        if (_owner is not { } owner) return;
        owner.Deactivated -= OwnerChanged;
        owner.StateChanged -= OwnerChanged;
        owner.LocationChanged -= OwnerChanged;
        owner.SizeChanged -= OwnerSizeChanged;
        owner.IsVisibleChanged -= OwnerVisibilityChanged;
        owner.Closing -= OwnerClosing;
        _owner = null;
    }
    private void OwnerChanged(object? sender, EventArgs e) => CloseMenu();
    private void OwnerSizeChanged(object sender, SizeChangedEventArgs e) => CloseMenu();
    private void OwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => CloseMenu();
    private void OwnerClosing(object? sender, CancelEventArgs e) => CloseMenu();
}
