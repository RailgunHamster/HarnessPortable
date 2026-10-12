using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace HarnessPortable.Windows.Controls;

public sealed class ConnectionGroup(string name, IEnumerable<object> children, bool expanded = true)
{
    public string Name { get; } = name;
    public List<object> Children { get; } = children.ToList();
    public bool Expanded { get; set; } = expanded;
}

public sealed class ConnectionRow(ConnectionEntry entry, string status, Brush brush)
{
    public ConnectionEntry Entry { get; } = entry;
    public string Status { get; } = status;
    public Brush StatusBrush { get; } = brush;
    public Visibility DisconnectVisibility => Entry.Tunnel is null ? Visibility.Collapsed : Visibility.Visible;
}

public partial class ConnectionSidebar : System.Windows.Controls.UserControl
{
    private AppServices? _services;
    private ConnectionLibrary? _library;
    public event Action<ConnectionEntry>? OpenRequested;
    public event Action<ConnectionEntry>? EditRequested;
    public event Action<ConnectionEntry>? DeleteRequested;
    public event Action<ConnectionEntry>? DisconnectRequested;
    public event Action? AddRequested;
    public event Action? SettingsRequested;

    public ConnectionSidebar()
    {
        InitializeComponent();
        Loaded += (_, _) => { Hook(true); Refresh(); };
        Unloaded += (_, _) => Hook(false);
    }

    public void Initialize(AppServices services)
    {
        _services = services;
        _library = new ConnectionLibrary(services.Profiles, services.Settings);
        if (IsLoaded) Hook(true);
        Refresh();
    }

    private void Hook(bool hook)
    {
        if (_services is null) return;
        _services.Profiles.Changed -= Refresh;
        _services.Settings.Changed -= Refresh;
        _services.Tunnels.StateChanged -= TunnelChanged;
        AppTheme.Changed -= Refresh;
        if (!hook) return;
        _services.Profiles.Changed += Refresh;
        _services.Settings.Changed += Refresh;
        _services.Tunnels.StateChanged += TunnelChanged;
        AppTheme.Changed += Refresh;
    }

    private void TunnelChanged(TunnelInfo _) => Refresh();

    public void Refresh()
    {
        if (_library is null || _services is null) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var entries = _library.Load(SearchBox.Text);
        ConnectionRow Row(ConnectionEntry e)
        {
            var state = e.Tunnel is { } tunnel ? _services.Tunnels.GetState(tunnel.Id).Status : TunnelStatus.Stopped;
            var label = state switch
            {
                TunnelStatus.Connected => "SSH · 已连接", TunnelStatus.Connecting => "SSH · 连接中…",
                TunnelStatus.Retrying => "SSH · 重连中", TunnelStatus.Failed => "SSH · 连接失败",
                _ => e.Tunnel is null ? "直连网页" : "SSH · 未连接",
            };
            return new(e, label, (Brush)FindResource(state == TunnelStatus.Connected ? "DshGreen" :
                state == TunnelStatus.Failed ? "DshRed" : "DshTextSecondary"));
        }
        var old = (ConnectionTree.ItemsSource as IEnumerable<ConnectionGroup>)?.ToDictionary(g => g.Name, g => g.Expanded);
        ConnectionGroup Group(string name, IEnumerable<object> rows, bool expanded = true) =>
            new(name, rows, old?.GetValueOrDefault(name, expanded) ?? expanded);
        ConnectionTree.ItemsSource = !string.IsNullOrWhiteSpace(SearchBox.Text)
            ? new[] { Group($"搜索结果 · {entries.Count}", entries.Select(Row)) }
            : new[]
            {
                Group("常用", entries.Where(e => e.Favorite).Select(Row)),
                Group("最近", entries.Where(e => e.LastOpened != null).OrderByDescending(e => e.LastOpened).Take(6).Select(Row)),
                Group("分组", entries.GroupBy(e => e.Group).OrderBy(g => g.Key).Select(g =>
                    new ConnectionGroup(g.Key, g.Select(Row), g.Key == "未分组"))),
            };
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => Refresh();
    private void Open_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is ConnectionRow row) OpenRequested?.Invoke(row.Entry); }
    private void Add_Click(object sender, RoutedEventArgs e) => AddRequested?.Invoke();
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private static ConnectionRow? RowFromMenu(object sender) => ((sender as MenuItem)?.Parent as ContextMenu)?.DataContext as ConnectionRow;
    private void Menu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.DataContext = (menu.PlacementTarget as FrameworkElement)?.Tag;
        if (menu.DataContext is ConnectionRow row && menu.Items[1] is MenuItem favorite)
            favorite.Header = row.Entry.Favorite ? "移出常用" : "加入常用";
    }
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ConnectionRow row } button || button.Parent is not Grid grid || grid.Children[0] is not Button open) return;
        open.ContextMenu.PlacementTarget = open;
        open.ContextMenu.IsOpen = true;
    }
    private void Edit_Click(object sender, RoutedEventArgs e) { if (RowFromMenu(sender) is { } row) EditRequested?.Invoke(row.Entry); }
    private void Delete_Click(object sender, RoutedEventArgs e) { if (RowFromMenu(sender) is { } row) DeleteRequested?.Invoke(row.Entry); }
    private void Disconnect_Click(object sender, RoutedEventArgs e) { if (RowFromMenu(sender) is { } row) DisconnectRequested?.Invoke(row.Entry); }
    private void Favorite_Click(object sender, RoutedEventArgs e) { if (RowFromMenu(sender) is { } row) _library?.Update(row.Entry.Key, p => p.Favorite = !row.Entry.Favorite); }
    private void Group_Click(object sender, RoutedEventArgs e)
    {
        if (RowFromMenu(sender) is not { } row) return;
        var dialog = new LayoutNameWindow { Owner = Window.GetWindow(this), Title = "设置连接分组" };
        if (dialog.ShowDialog() == true) _library?.Update(row.Entry.Key, p => p.Group = dialog.LayoutName);
    }
}
