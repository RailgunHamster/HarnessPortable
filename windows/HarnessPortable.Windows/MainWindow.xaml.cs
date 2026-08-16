using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows;

public partial class MainWindow : Window
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

    private readonly Dictionary<string, TunnelProfile> _pendingProfiles = [];
    private readonly Dictionary<string, BrowserWindow> _tunnelWindows = [];
    private readonly List<BrowserWindow> _directWindows = [];

    private bool _trayHintShown;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        TunnelList.ItemsSource = _tunnelItems;
        DirectList.ItemsSource = _directItems;

        _services.Tunnels.StateChanged += OnTunnelStateChanged;
        RefreshLists();
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
            OpenBrowserForTunnel(pending, info.LocalPort);
        }
        else if (info.ProfileId is not null &&
                 info.Status is TunnelStatus.Failed or TunnelStatus.Stopped)
        {
            _pendingProfiles.Remove(info.ProfileId);
        }

        RefreshLists();
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

        UpdateStatusBar();
    }

    private void UpdateStatusBar()
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

    private void StopTunnel_Click(object sender, RoutedEventArgs e)
    {
        _pendingProfiles.Clear();
        _services.Tunnels.StopAll();

        foreach (var window in _tunnelWindows.Values.ToList())
        {
            window.Close();
        }

        _tunnelWindows.Clear();
        RefreshLists();
    }

    private void TunnelStop_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TunnelListItem item)
        {
            return;
        }

        _pendingProfiles.Remove(item.Profile.Id);
        _services.Tunnels.Stop(item.Profile.Id);

        if (_tunnelWindows.TryGetValue(item.Profile.Id, out var window))
        {
            window.Close();
            _tunnelWindows.Remove(item.Profile.Id);
        }

        RefreshLists();
    }

    private void AddTunnel_Click(object sender, RoutedEventArgs e)
    {
        var editor = new TunnelEditorWindow(null) { Owner = this };
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
            ConnectTunnel(item.Profile);
        }
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
            OpenBrowserForTunnel(profile, state.LocalPort);
            return;
        }

        _pendingProfiles[profile.Id] = profile;
        _services.Tunnels.Start(profile.Id);
    }

    private void TunnelEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TunnelListItem item)
        {
            return;
        }

        var editor = new TunnelEditorWindow(item.Profile) { Owner = this };
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
            this,
            $"确定删除隧道“{item.Name}”吗？",
            "删除隧道",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        _pendingProfiles.Remove(item.Profile.Id);
        _services.Tunnels.Stop(item.Profile.Id);

        if (_tunnelWindows.TryGetValue(item.Profile.Id, out var window))
        {
            window.Close();
            _tunnelWindows.Remove(item.Profile.Id);
        }

        var profiles = _services.Profiles.LoadTunnels();
        profiles.RemoveAll(p => p.Id == item.Profile.Id);
        _services.Profiles.SaveTunnels(profiles);
        _services.Secrets.ClearPassword(item.Profile.Id);

        RefreshLists();
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
            OpenBrowserDirect(item.Url);
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

    private void OpenBrowserForTunnel(TunnelProfile profile, int localPort)
    {
        if (_tunnelWindows.TryGetValue(profile.Id, out var existing) && existing.IsLoaded)
        {
            existing.Activate();
            return;
        }

        _tunnelWindows.Remove(profile.Id);

        var window = new BrowserWindow(_services, profile, localPort) { Owner = this };
        window.Closed += (_, _) => _tunnelWindows.Remove(profile.Id);
        _tunnelWindows[profile.Id] = window;
        window.Show();
    }

    private void OpenBrowserDirect(string url)
    {
        // Direct URLs may be opened any number of times: every click gets a
        // fresh browser window.
        var window = new BrowserWindow(_services, url) { Owner = this };
        window.Closed += (_, _) => _directWindows.Remove(window);
        _directWindows.Add(window);
        window.Show();
    }

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
