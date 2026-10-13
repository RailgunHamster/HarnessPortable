using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using HarnessPortable.Windows.Services;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace HarnessPortable.Windows.Controls;

public sealed class ConnectionRow(ConnectionEntry entry, string detail, Brush brush, bool canDisconnect)
{
    private static readonly Geometry ServerIcon = FrozenIcon("M2,1 L16,1 L16,7 L2,7 Z M2,11 L16,11 L16,17 L2,17 Z M5,4 L7,4 M5,14 L7,14");
    private static readonly Geometry WebIcon = FrozenIcon("M9,1 A8,8 0 1 1 9,17 A8,8 0 1 1 9,1 M1,9 L17,9 M9,1 C3,5 3,13 9,17 M9,1 C15,5 15,13 9,17");
    public ConnectionEntry Entry { get; } = entry;
    public string Detail { get; } = detail;
    public Brush StatusBrush { get; } = brush;
    public Geometry Icon => Entry.Tunnel is null ? WebIcon : ServerIcon;
    public string Tooltip => Entry.Name + "\n" + Entry.Address + "\n" + Detail;
    public Visibility DisconnectVisibility => canDisconnect ? Visibility.Visible : Visibility.Collapsed;
    private static Geometry FrozenIcon(string path) { var icon = Geometry.Parse(path); icon.Freeze(); return icon; }
}

public partial class ConnectionSidebar : System.Windows.Controls.UserControl
{
    private AppServices? _services;
    private ConnectionLibrary? _library;
    private ConnectionLibraryFilter _filter;
    private string? _activeKey;
    private bool _resetScroll;
    private readonly Dictionary<string, bool> _groupExpansion = [];
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
        _services = services; _library = new ConnectionLibrary(services.Profiles, services.Settings);
        if (IsLoaded) Hook(true);
        Refresh();
    }
    private void Hook(bool hook)
    {
        if (_services is null) return;
        _services.Profiles.Changed -= Refresh; _services.Settings.Changed -= Refresh;
        _services.Tunnels.StateChanged -= TunnelChanged; AppTheme.Changed -= Refresh;
        if (!hook) return;
        _services.Profiles.Changed += Refresh; _services.Settings.Changed += Refresh;
        _services.Tunnels.StateChanged += TunnelChanged; AppTheme.Changed += Refresh;
    }
    private void TunnelChanged(TunnelInfo _) => Refresh();
    public void SetActiveConnection(string? key)
    {
        _activeKey = key;
        ConnectionList.SelectedItem = ConnectionList.Items.OfType<ConnectionRow>().FirstOrDefault(row => row.Entry.Key == key);
    }
    public void Refresh()
    {
        if (_library is null || _services is null) return;
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        var entries = _library.Load(SearchBox.Text, _filter);
        var scroll = FindScrollViewer(ConnectionList);
        var offset = _resetScroll ? 0 : scroll?.VerticalOffset ?? 0;
        _resetScroll = false;
        var rows = entries.Select(Row).ToList();
        var view = new ListCollectionView(rows);
        if (_filter == ConnectionLibraryFilter.All && entries.Any(e => e.Group != "未分组"))
        {
            view.SortDescriptions.Add(new("Entry.Group", System.ComponentModel.ListSortDirection.Ascending));
            view.SortDescriptions.Add(new("Entry.Name", System.ComponentModel.ListSortDirection.Ascending));
            view.GroupDescriptions.Add(new PropertyGroupDescription("Entry.Group"));
        }
        ConnectionList.ItemsSource = view;
        SetActiveConnection(_activeKey);
        ConnectionCount.Text = $"{entries.Count} 个连接";
        EmptyState.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var searching = !string.IsNullOrWhiteSpace(SearchBox.Text);
        EmptyTitle.Text = searching ? "没有匹配的连接" : _filter switch
        { ConnectionLibraryFilter.Favorites => "还没有常用连接", ConnectionLibraryFilter.Recent => "还没有最近使用的连接", _ => "添加第一个连接" };
        EmptyHint.Text = searching ? "试试连接名称、服务器地址或分组。" : _filter switch
        { ConnectionLibraryFilter.Favorites => "在连接菜单中选择“加入常用”。", ConnectionLibraryFilter.Recent => "打开过的连接会出现在这里。", _ => "点击右上角 ＋ 添加 SSH 隧道或直连网页。" };
        Dispatcher.BeginInvoke(() => FindScrollViewer(ConnectionList)?.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);

        ConnectionRow Row(ConnectionEntry entry)
        {
            var state = entry.Tunnel is { } tunnel ? _services.Tunnels.GetState(tunnel.Id).Status : TunnelStatus.Stopped;
            var status = state switch
            { TunnelStatus.Connected => "已连接", TunnelStatus.Connecting => "连接中…", TunnelStatus.Retrying => "重连中…", TunnelStatus.Failed => "连接失败", _ => "未连接" };
            var address = entry.Tunnel?.SshHost;
            var detail = entry.Tunnel is null ? "直连网页" : status + " · " + (entry.Name == address ? "SSH" : address);
            var color = state switch { TunnelStatus.Connected => "DshGreen", TunnelStatus.Failed => "DshRed", _ => "DshTextTertiary" };
            return new(entry, detail, (Brush)FindResource(color), entry.Tunnel is not null && state is TunnelStatus.Connected or TunnelStatus.Connecting or TunnelStatus.Retrying);
        }
    }
    private static ScrollViewer? FindScrollViewer(DependencyObject node)
    {
        if (node is ScrollViewer viewer) return viewer;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
            if (FindScrollViewer(VisualTreeHelper.GetChild(node, index)) is { } child) return child;
        return null;
    }
    private void Search_Changed(object sender, TextChangedEventArgs e) { _resetScroll = true; Refresh(); }
    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        _filter = ((sender as FrameworkElement)?.Tag as string) switch
        { "favorites" => ConnectionLibraryFilter.Favorites, "recent" => ConnectionLibraryFilter.Recent, _ => ConnectionLibraryFilter.All };
        if (ConnectionList is null) return;
        _resetScroll = true; Refresh();
    }
    private void Group_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { DataContext: CollectionViewGroup group } expander)
            expander.IsExpanded = _groupExpansion.GetValueOrDefault(group.Name?.ToString() ?? "", true);
    }
    private void Group_ExpansionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { IsLoaded: true, DataContext: CollectionViewGroup group } expander)
            _groupExpansion[group.Name?.ToString() ?? ""] = expander.IsExpanded;
    }
    private void Open(ConnectionRow row) { SetActiveConnection(row.Entry.Key); OpenRequested?.Invoke(row.Entry); }
    private void Open_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is ConnectionRow row) Open(row); }
    private void Connections_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    { if (e.Key == Key.Enter && ConnectionList.SelectedItem is ConnectionRow row) { Open(row); e.Handled = true; } }
    private void Add_Click(object sender, RoutedEventArgs e) => AddRequested?.Invoke();
    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    private static ConnectionRow? RowFromMenu(object sender) => ((sender as MenuItem)?.Parent as ContextMenu)?.DataContext as ConnectionRow;
    private void Menu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        menu.DataContext = (menu.PlacementTarget as FrameworkElement)?.DataContext;
        if (menu.DataContext is ConnectionRow row && menu.Items[1] is MenuItem favorite)
            favorite.Header = row.Entry.Favorite ? "移出常用" : "加入常用";
    }
    private void More_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Parent: Grid grid } || grid.ContextMenu is null) return;
        grid.ContextMenu.PlacementTarget = grid; grid.ContextMenu.IsOpen = true;
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
