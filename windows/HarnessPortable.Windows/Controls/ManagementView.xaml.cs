using System.Collections.ObjectModel;
using System.ComponentModel;
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

    /// <summary>
    /// Must declare the interface (not just raise the event): WPF's binding
    /// engine only subscribes to change notifications when it can cast the
    /// source to <see cref="INotifyPropertyChanged"/>. Without it the auth-mode
    /// button saved the new mode but its label never refreshed, so the toggle
    /// looked dead and extra clicks flipped the mode back off.
    /// </summary>
    public sealed class DirectListItem : INotifyPropertyChanged
    {
        public required string Url { get; init; }
        public string Host => ProfileStore.HostOf(Url);

        /// <summary>Row label for the per-URL auth mode: "无" or "NSSM 自动".</summary>
        public string AuthLabel
        {
            get;
            set
            {
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AuthLabel)));
            }
        } = "无";

        public event PropertyChangedEventHandler? PropertyChanged;
    }

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

        // The panel lives in its own window now, so it is unloaded whenever that
        // window closes and loaded again when it is reopened: hook and unhook
        // symmetrically, and refresh what the ctor cannot keep current.
        Loaded += (_, _) =>
        {
            _services.Tunnels.StateChanged -= OnTunnelStateChanged;
            _services.Tunnels.StateChanged += OnTunnelStateChanged;
            AppTheme.Changed -= RefreshLists;
            AppTheme.Changed += RefreshLists;
            HookUpdateEvents(true);
            RefreshLists();
            RefreshUpdatePanel();
        };
        Unloaded += (_, _) =>
        {
            _services.Tunnels.StateChanged -= OnTunnelStateChanged;
            AppTheme.Changed -= RefreshLists;
            _discoveryCts?.Cancel();
            HookUpdateEvents(false);
        };
        InitializeCloseBehaviorSettings();
        InitializeUpdateSettings();
        RefreshLists();
    }

    private bool _updateEventsHooked;

    private void HookUpdateEvents(bool hook)
    {
        if (hook == _updateEventsHooked)
        {
            return;
        }

        _updateEventsHooked = hook;
        if (hook)
        {
            _services.Updates.StateChanged += OnUpdateStateChanged;
        }
        else
        {
            _services.Updates.StateChanged -= OnUpdateStateChanged;
        }
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
                    // The coloured dot is drawn next to this label (see the XAML),
                    // so the text itself carries no glyph.
                    TunnelStatus.Connected => "已连接",
                    TunnelStatus.Connecting => "连接中…",
                    TunnelStatus.Retrying => "重连中",
                    TunnelStatus.Failed => "失败",
                    _ => "",
                },
                StatusBrush = state.Status switch
                {
                    TunnelStatus.Connected => (System.Windows.Media.Brush)FindResource("DshGreen"),
                    TunnelStatus.Failed => (System.Windows.Media.Brush)FindResource("DshRed"),
                    _ => (System.Windows.Media.Brush)FindResource("DshAccent"),
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
            _directItems.Add(new DirectListItem
            {
                Url = url,
                AuthLabel = DirectAuthModeLabel(url),
            });
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

    private void InitializeUpdateSettings()
    {
        _suppressSettings = true;
        var settings = _services.Settings.Load();
        var selection = UpdateSelection.From(settings);
        UpdateSourceBox.SelectedItem = UpdateSourceBox.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == UpdateSelection.Tag(selection.Slot));
        UpdateServerBox.Text = selection.Url();
        VersionText.Text = "当前版本 " + _services.Updates.CurrentVersion
            + " · " + _services.Updates.DeploymentLabel;
        RefreshUpdatePanel();
        _suppressSettings = false;
    }

    /// <summary>
    /// Reads both slots out of the UI. The address box always belongs to the
    /// slot the picker is on, so the other slot keeps whatever it had.
    /// </summary>
    private UpdateSelection LoadUpdateSelection()
    {
        var stored = UpdateSelection.From(_services.Settings.Load());
        var slot = UpdateSelection.ParseSlot((UpdateSourceBox.SelectedItem as ComboBoxItem)?.Tag as string) ?? UpdateSourceSlot.Gitee;
        var typed = UpdateServerBox.Text.Trim();
        return stored.WithUrl(slot, typed) with { Slot = slot };
    }

    private void UpdateSourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettings)
        {
            return;
        }

        // The address box still shows the slot we are LEAVING — file its text
        // there. Reading it after the switch (2.6.19–2.6.23) wrote the old
        // slot's address into the newly picked slot, so switching appeared to
        // do nothing at all.
        var stored = UpdateSelection.From(_services.Settings.Load());
        var typed = UpdateServerBox.Text.Trim();
        var leavingTag = (e.RemovedItems.Cast<ComboBoxItem>().FirstOrDefault()?.Tag as string)?.Trim();
        var selection = UpdateSelection.MergeTyped(stored, leavingTag, typed, (UpdateSourceBox.SelectedItem as ComboBoxItem)?.Tag as string);

        var settings = _services.Settings.Load();
        selection.Store(settings);
        _services.Settings.Save(settings);

        _suppressSettings = true;
        UpdateSourceBox.SelectedItem = UpdateSourceBox.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == UpdateSelection.Tag(selection.Slot));
        UpdateServerBox.Text = selection.Url();
        _suppressSettings = false;
    }

    private void OnUpdateStateChanged()
    {
        if (Dispatcher.CheckAccess())
        {
            RefreshUpdatePanel();
        }
        else
        {
            Dispatcher.BeginInvoke(RefreshUpdatePanel);
        }
    }

    private void RefreshUpdatePanel()
    {
        var updates = _services.Updates;
        UpdateStatusText.Text = updates.Status;
        ChangelogBox.Text = updates.ChangelogText;

        // Real download progress, not just the "42%" inside the status line.
        UpdateProgressBar.Value = updates.Progress;
        UpdateProgressBar.Visibility = updates.Busy && updates.Progress > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        CheckUpdateButton.IsEnabled = !updates.Busy;
        ApplyUpdateButton.IsEnabled = !updates.Busy && updates.CanApply;
        ApplyUpdateButton.Content = updates.CanApply && updates.AvailableVersion is { } version
            ? $"下载 {version} 并重启"
            : "下载并重启";
        HeaderUpdateButton.IsEnabled = !updates.Busy;
        HeaderUpdateButton.Content = updates.CanApply && updates.AvailableVersion is { } ver
            ? $"更新 {ver}"
            : "检查更新";
    }

    private async void HeaderUpdate_Click(object sender, RoutedEventArgs e)
    {
        ManagementTabs.SelectedIndex = 2;
        if (_services.Updates.CanApply)
        {
            ApplyUpdate_Click(sender, e);
        }
        else
        {
            await _services.Updates.CheckAsync(CurrentUpdateServerUrl());
        }
    }

    private string CurrentUpdateServerUrl()
    {
        var selection = LoadUpdateSelection();
        var typed = selection.Url();
        return typed.Length == 0 ? UpdateSourceFactory.DefaultServerUrl : typed;
    }

    private void SaveUpdateServer_Click(object sender, RoutedEventArgs e)
    {
        var settings = _services.Settings.Load();
        LoadUpdateSelection().Store(settings);
        _services.Settings.Save(settings);
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        SaveUpdateServer_Click(sender, e);
        await _services.Updates.CheckAsync(CurrentUpdateServerUrl());
    }

    private async void ApplyUpdate_Click(object sender, RoutedEventArgs e)
    {
        SaveUpdateServer_Click(sender, e);
        await _services.Updates.ApplyAsync(CurrentUpdateServerUrl());
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
        var sshConfigPath = _services.Settings.Load().SshConfigPath;
        var editor = new TunnelEditorWindow(null, sshConfigPath) { Owner = Window.GetWindow(this) };
        if (editor.ShowDialog() == true && editor.Result is { } result)
        {
            SaveSshConfigPath(result.SshConfigPath);
            var profiles = _services.Profiles.LoadTunnels();
            profiles.RemoveAll(p => p.Id == result.Profile.Id);
            profiles.Insert(0, result.Profile);
            _services.Profiles.SaveTunnels(profiles);

            if (!string.IsNullOrEmpty(result.Password))
            {
                _services.Secrets.SetPassword(result.Profile.Id, result.Password);
            }

            SaveAuthInput(result);

            RefreshLists();
        }
    }

    /// <summary>
    /// The editor's web-login field is authoritative: a blank value clears the
    /// stored one. It lives in the credential store, never in profiles.json.
    /// </summary>
    private void SaveAuthInput(TunnelEditorWindow.EditorResult result)
    {
        var value = result.AuthInput?.Trim() ?? "";
        if (value.Length == 0)
        {
            _services.Secrets.ClearAuthInput(result.Profile.Id);
            return;
        }

        _services.Secrets.SetAuthInput(result.Profile.Id, value);
    }

    private void SaveSshConfigPath(string? path)
    {
        var settings = _services.Settings.Load();
        var normalized = path?.Trim() ?? "";
        if (string.Equals(settings.SshConfigPath, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        settings.SshConfigPath = normalized;
        _services.Settings.Save(settings);
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

        var sshConfigPath = _services.Settings.Load().SshConfigPath;
        var editor = new TunnelEditorWindow(
            item.Profile, sshConfigPath, _services.Secrets.GetAuthInput(item.Profile.Id))
        {
            Owner = Window.GetWindow(this),
        };
        if (editor.ShowDialog() == true && editor.Result is { } result)
        {
            SaveSshConfigPath(result.SshConfigPath);
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

            SaveAuthInput(result);

            RefreshLists();
        }
    }

    private void TunnelDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TunnelListItem item)
        {
            return;
        }

        var confirm = HarnessPortable.Windows.Automation.AutomationDialogs.Show(
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
        _services.Secrets.ClearAuthInput(item.Profile.Id);

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

        // The auth mode is keyed by URL and would otherwise linger as a dead
        // entry once the address is gone (and silently reactivate if the same
        // URL is added back later without the user noticing).
        var settings = _services.Settings.Load();
        if (settings.DirectAuthModes.Remove(item.Url))
        {
            _services.Settings.Save(settings);
        }

        RefreshLists();
    }

    /// <summary>Row label for the URL's saved auth mode.</summary>
    private string DirectAuthModeLabel(string url) =>
        _services.Settings.Load().DirectAuthModes.TryGetValue(url, out var mode) &&
        mode == LocalNssmAuth.ModeNssm
            ? "NSSM 自动"
            : "无";

    /// <summary>
    /// Cycles a direct URL's auth mode between "none" and the local NSSM
    /// auto-login, stored per-URL in settings.json (profiles.json's directs
    /// list stays a plain cross-platform string array).
    /// </summary>
    private void DirectAuthToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not DirectListItem item)
        {
            return;
        }

        var settings = _services.Settings.Load();
        var enabled = settings.DirectAuthModes.TryGetValue(item.Url, out var mode) &&
            mode == LocalNssmAuth.ModeNssm;

        if (enabled)
        {
            settings.DirectAuthModes.Remove(item.Url);
        }
        else
        {
            settings.DirectAuthModes[item.Url] = LocalNssmAuth.ModeNssm;
        }

        _services.Settings.Save(settings);
        item.AuthLabel = enabled ? "无" : "NSSM 自动";
    }
}
