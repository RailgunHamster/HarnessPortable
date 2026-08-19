using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Controls;

public partial class ManagementView : System.Windows.Controls.UserControl
{
    public sealed class TunnelListItem
    {
        public required TunnelProfile Profile { get; init; }
        public string Name => Profile.DisplayName;
        public string Summary => Profile.Summary;
        public string LocalText => $"本地端口 {Profile.LocalPort}";
        public string StatusText { get; init; } = "";
        public System.Windows.Media.Brush StatusBrush { get; init; } = System.Windows.Media.Brushes.Green;
        public Visibility StatusVisibility { get; init; } = Visibility.Collapsed;
        public Visibility StopVisibility { get; init; } = Visibility.Collapsed;
    }

    public sealed class DirectListItem
    {
        public required string Url { get; init; }
        public string Host => ProfileStore.HostOf(Url);
    }

    private static readonly System.Windows.Media.Brush GreenBrush =
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x05, 0x96, 0x69));
    private static readonly System.Windows.Media.Brush BlueBrush =
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB));
    private static readonly System.Windows.Media.Brush RedBrush =
        new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDC, 0x26, 0x26));

    private readonly AppServices _services;
    private readonly ObservableCollection<TunnelListItem> _tunnelItems = [];
    private readonly ObservableCollection<DirectListItem> _directItems = [];
    private readonly ObservableCollection<string> _lanMachines = [];
    private CancellationTokenSource? _discoveryCts;
    private bool _suppressSettings;

    public event Action<TunnelProfile>? TunnelConnectRequested;
    public event Action<string>? TunnelStopRequested;
    public event Action<string>? DirectOpenRequested;

    public ManagementView(AppServices services)
    {
        _services = services;
        InitializeComponent();

        TunnelList.ItemsSource = _tunnelItems;
        DirectList.ItemsSource = _directItems;
        DirectInput.ItemsSource = _lanMachines;

        _services.Tunnels.StateChanged += OnTunnelStateChanged;
        Unloaded += (_, _) => _discoveryCts?.Cancel();
        InitializeCloseBehaviorSettings();
        RefreshLists();
    }

    private void OnTunnelStateChanged(TunnelInfo info)
    {
        if (Dispatcher.CheckAccess())
        {
            RefreshLists();
        }
        else
        {
            Dispatcher.BeginInvoke(RefreshLists);
        }
    }

    private void RefreshLists()
    {
        _tunnelItems.Clear();
        foreach (var profile in _services.Profiles.LoadTunnels())
        {
            var state = _services.Tunnels.GetState(profile.Id);
            var active = state.Status is TunnelStatus.Connected or TunnelStatus.Connecting or TunnelStatus.Retrying;

            _tunnelItems.Add(new TunnelListItem
            {
                Profile = profile,
                StatusText = state.Status switch
                {
                    TunnelStatus.Connected => "● 已连接",
                    TunnelStatus.Connecting => "● 连接中…",
                    TunnelStatus.Retrying => "● 重连中",
                    TunnelStatus.Failed => "● 失败",
                    _ => "",
                },
                StatusBrush = state.Status switch
                {
                    TunnelStatus.Connected => GreenBrush,
                    TunnelStatus.Failed => RedBrush,
                    _ => BlueBrush,
                },
                StatusVisibility = state.Status is TunnelStatus.Connected or TunnelStatus.Connecting
                    or TunnelStatus.Retrying or TunnelStatus.Failed
                    ? Visibility.Visible
                    : Visibility.Collapsed,
                StopVisibility = active ? Visibility.Visible : Visibility.Collapsed,
            });
        }

        _directItems.Clear();
        foreach (var url in _services.Profiles.LoadDirects())
        {
            _directItems.Add(new DirectListItem { Url = url });
        }

        TunnelCountText.Text = _tunnelItems.Count.ToString();
        EmptyTunnelsHint.Visibility = _tunnelItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyDirectHint.Visibility = _directItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InitializeCloseBehaviorSettings()
    {
        _suppressSettings = true;

        CloseBehaviorBox.Items.Add(new ComboBoxItem { Content = "直接退出程序", Tag = "exit" });
        CloseBehaviorBox.Items.Add(new ComboBoxItem { Content = "最小化到托盘", Tag = "tray" });

        var current = _services.Settings.Load().CloseBehavior;
        CloseBehaviorBox.SelectedItem = CloseBehaviorBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals(i.Tag as string, current, StringComparison.OrdinalIgnoreCase))
            ?? CloseBehaviorBox.Items[0];

        RestoreLayoutBox.IsChecked = _services.Settings.Load().RestoreLastLayoutOnStartup;

        _suppressSettings = false;
    }

    private void CloseBehaviorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettings || CloseBehaviorBox.SelectedItem is not ComboBoxItem item ||
            item.Tag is not string value)
        {
            return;
        }

        var settings = _services.Settings.Load();
        settings.CloseBehavior = value;
        _services.Settings.Save(settings);
    }

    private void RestoreLayoutBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettings)
        {
            return;
        }

        var settings = _services.Settings.Load();
        settings.RestoreLastLayoutOnStartup = RestoreLayoutBox.IsChecked == true;
        _services.Settings.Save(settings);
    }

    private void AddTunnel_Click(object sender, RoutedEventArgs e)
    {
        var editor = new TunnelEditorWindow(null) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true && editor.Result is { } result)
        {
            var profiles = _services.Profiles.LoadTunnels();
            profiles.RemoveAll(p => p.Id == result.Profile.Id);
            profiles.Insert(0, result.Profile);
            _services.Profiles.SaveTunnels(profiles);

            if (!string.IsNullOrEmpty(result.Password))
            {
                _services.Secrets.SetPassword(result.Profile.Id, result.Password);
            }

            RefreshLists();
        }
    }

    private void TunnelConnect_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TunnelListItem item)
        {
            TunnelConnectRequested?.Invoke(item.Profile);
        }
    }

    private void TunnelStop_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is TunnelListItem item)
        {
            StopTunnel(item.Profile.Id);
        }
    }

    private void StopTunnel(string profileId)
    {
        _services.Tunnels.Stop(profileId);
        TunnelStopRequested?.Invoke(profileId);
        RefreshLists();
    }

    private void TunnelEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TunnelListItem item)
        {
            return;
        }

        var editor = new TunnelEditorWindow(item.Profile) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true && editor.Result is { } result)
        {
            var profiles = _services.Profiles.LoadTunnels();
            var index = profiles.FindIndex(p => p.Id == result.Profile.Id);
            if (index >= 0)
            {
                profiles[index] = result.Profile;
            }

            _services.Profiles.SaveTunnels(profiles);

            if (!string.IsNullOrEmpty(result.Password))
            {
                _services.Secrets.SetPassword(result.Profile.Id, result.Password);
            }

            RefreshLists();
        }
    }

    private void TunnelDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TunnelListItem item)
        {
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            Window.GetWindow(this),
            $"确定删除隧道“{item.Name}”吗？",
            "删除隧道",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        StopTunnel(item.Profile.Id);

        var profiles = _services.Profiles.LoadTunnels();
        profiles.RemoveAll(p => p.Id == item.Profile.Id);
        _services.Profiles.SaveTunnels(profiles);
        _services.Secrets.ClearPassword(item.Profile.Id);

        RefreshLists();
    }

    private async void DirectInput_DropDownOpened(object sender, EventArgs e)
    {
        await LoadLanMachinesAsync(force: false);
    }

    private async void RefreshDirectMachines_Click(object sender, RoutedEventArgs e)
    {
        await LoadLanMachinesAsync(force: true);
        DirectInput.IsDropDownOpen = true;
    }

    private async Task LoadLanMachinesAsync(bool force)
    {
        if (force)
        {
            HostResolver.ResetLanMachineDiscovery();
        }

        _discoveryCts?.Cancel();
        _discoveryCts?.Dispose();
        var cts = new CancellationTokenSource();
        _discoveryCts = cts;

        try
        {
            var machines = await HostResolver.DiscoverLanMachinesAsync(cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            var text = DirectInput.Text;
            _lanMachines.Clear();
            foreach (var machine in machines)
            {
                _lanMachines.Add(machine.Name);
            }

            DirectInput.SelectedIndex = -1;
            DirectInput.Text = text;
        }
        catch (OperationCanceledException)
        {
            // The window was unloaded or another refresh superseded this one.
        }
        finally
        {
            if (ReferenceEquals(_discoveryCts, cts))
            {
                _discoveryCts = null;
            }

            cts.Dispose();
        }
    }

    private void DirectInput_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DirectInput.SelectedItem is string name)
        {
            DirectInput.Text = name;
        }
    }

    private void AddDirect_Click(object sender, RoutedEventArgs e)
    {
        var normalized = ProfileStore.NormalizeUrl(DirectInput.Text);
        if (normalized is null)
        {
            return;
        }

        var directs = _services.Profiles.LoadDirects();
        directs.RemoveAll(u => u == normalized);
        directs.Insert(0, normalized);
        _services.Profiles.SaveDirects(directs);
        DirectInput.Text = "";
        RefreshLists();
    }

    private void DirectConnect_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is DirectListItem item)
        {
            DirectOpenRequested?.Invoke(item.Url);
        }
    }

    private void DirectDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DirectListItem item)
        {
            return;
        }

        var directs = _services.Profiles.LoadDirects();
        directs.RemoveAll(u => u == item.Url);
        _services.Profiles.SaveDirects(directs);
        RefreshLists();
    }
}
