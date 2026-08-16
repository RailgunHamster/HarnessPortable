using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
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
        public string StatusText => "● 已连接";
        public Visibility StatusVisibility { get; init; } = Visibility.Collapsed;
    }

    public sealed class DirectListItem
    {
        public required string Url { get; init; }
        public string Host => ProfileStore.HostOf(Url);
    }

    private readonly AppServices _services;
    private readonly ObservableCollection<TunnelListItem> _tunnelItems = [];
    private readonly ObservableCollection<DirectListItem> _directItems = [];
    private TunnelProfile? _pendingProfile;
    private BrowserWindow? _browserWindow;
    private bool _trayHintShown;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();

        TunnelList.ItemsSource = _tunnelItems;
        DirectList.ItemsSource = _directItems;

        _services.Tunnel.State.StateChanged += OnTunnelStateChanged;
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
        StatusText.Text = info.Status switch
        {
            TunnelStatus.Connected => $"{info.ProfileName} 已连接 · {info.Message}",
            TunnelStatus.Connecting => $"正在连接 {info.ProfileName}…",
            TunnelStatus.Retrying => $"重连中：{info.Message}",
            TunnelStatus.Failed => $"失败：{info.Message}",
            TunnelStatus.Stopped => info.Message ?? "隧道已停止",
            _ => "就绪",
        };

        StopTunnelButton.Visibility = info.Status is TunnelStatus.Connected or TunnelStatus.Connecting or TunnelStatus.Retrying
            ? Visibility.Visible
            : Visibility.Collapsed;

        RefreshLists();

        var pending = _pendingProfile;
        if (pending is not null &&
            info.ProfileId == pending.Id &&
            info.Status == TunnelStatus.Connected)
        {
            _pendingProfile = null;
            OpenBrowserForTunnel(pending, info.LocalPort);
        }
    }

    private void RefreshLists()
    {
        var connectedId = _services.Tunnel.State.Current.Status == TunnelStatus.Connected
            ? _services.Tunnel.State.Current.ProfileId
            : null;

        _tunnelItems.Clear();
        foreach (var profile in _services.Profiles.LoadTunnels())
        {
            _tunnelItems.Add(new TunnelListItem
            {
                Profile = profile,
                StatusVisibility = profile.Id == connectedId ? Visibility.Visible : Visibility.Collapsed,
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

    private void StopTunnel_Click(object sender, RoutedEventArgs e)
    {
        _pendingProfile = null;
        _services.Tunnel.Stop();
        StatusText.Text = "隧道已断开";
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
        if ((sender as FrameworkElement)?.Tag is not TunnelListItem item)
        {
            return;
        }

        ConnectTunnel(item.Profile);
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

        var current = _services.Tunnel.State.Current;
        if (current.ProfileId == profile.Id && current.Status == TunnelStatus.Connected)
        {
            OpenBrowserForTunnel(profile, current.LocalPort);
            return;
        }

        _pendingProfile = profile;
        _services.Tunnel.Start(profile.Id);
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

        var profiles = _services.Profiles.LoadTunnels();
        profiles.RemoveAll(p => p.Id == item.Profile.Id);
        _services.Profiles.SaveTunnels(profiles);
        _services.Secrets.ClearPassword(item.Profile.Id);

        if (_services.Tunnel.State.Current.ProfileId == item.Profile.Id)
        {
            _services.Tunnel.Stop();
        }

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
        _browserWindow?.Close();
        _browserWindow = new BrowserWindow(_services, profile, localPort) { Owner = this };
        _browserWindow.Closed += (_, _) => _browserWindow = null;
        _browserWindow.Show();
    }

    private void OpenBrowserDirect(string url)
    {
        _browserWindow?.Close();
        _browserWindow = new BrowserWindow(_services, url) { Owner = this };
        _browserWindow.Closed += (_, _) => _browserWindow = null;
        _browserWindow.Show();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);

        if (System.Windows.Application.Current is App { IsExiting: true })
        {
            return;
        }

        // Desktop convention: closing the window keeps the tunnel alive in
        // the tray; exit explicitly from the tray menu.
        e.Cancel = true;
        Hide();

        if (!_trayHintShown)
        {
            _trayHintShown = true;
            System.Windows.Forms.MessageBox.Show(
                "Harness Portable 仍在托盘运行。\n双击托盘图标可重新打开窗口，右键图标可退出。",
                "Harness Portable",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Information);
        }
    }
}
