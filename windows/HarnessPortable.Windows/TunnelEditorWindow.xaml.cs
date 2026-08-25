using System.Collections.ObjectModel;
using System.Windows;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows;

public partial class TunnelEditorWindow : Window
{
    public sealed record EditorResult(TunnelProfile Profile, string? Password);

    private sealed record HostSuggestion(
        string Title,
        string Detail,
        string Value,
        SshConfigHost? Config);

    private readonly TunnelProfile? _initial;
    private readonly ObservableCollection<HostSuggestion> _hostSuggestions = [];
    private CancellationTokenSource? _discoveryCts;

    public EditorResult? Result { get; private set; }

    public TunnelEditorWindow(TunnelProfile? initial)
    {
        _initial = initial;
        InitializeComponent();
        HostBox.ItemsSource = _hostSuggestions;
        Closed += (_, _) => _discoveryCts?.Cancel();

        if (initial is not null)
        {
            Title = "编辑 SSH 隧道";
            NameBox.Text = initial.Name;
            HostBox.Text = initial.SshHost;
            SshPortBox.Text = initial.SshPort.ToString();
            UserBox.Text = initial.User;
            RemoteHostBox.Text = initial.RemoteHost;
            RemotePortBox.Text = initial.RemotePort.ToString();
            LocalPortBox.Text = initial.LocalPort.ToString();
            PasswordCaption.Text = "密码（留空保持不变）";
        }
    }

    private async void HostBox_DropDownOpened(object sender, EventArgs e)
    {
        await LoadHostSuggestionsAsync(force: false);
    }

    private async void RefreshMachines_Click(object sender, RoutedEventArgs e)
    {
        await LoadHostSuggestionsAsync(force: true);
        HostBox.IsDropDownOpen = true;
    }

    private async Task LoadHostSuggestionsAsync(bool force)
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
            var configHosts = await Task.Run(SshConfigReader.LoadDefault, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            var text = HostBox.Text;
            _hostSuggestions.Clear();
            foreach (var configHost in configHosts)
            {
                _hostSuggestions.Add(new HostSuggestion(
                    configHost.Alias,
                    $"SSH 配置 · {configHost.ConnectionLabel}",
                    configHost.HostName,
                    configHost));
            }

            HostBox.SelectedIndex = -1;
            HostBox.Text = text;

            var machines = await HostResolver.DiscoverLanMachinesAsync(cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            foreach (var machine in machines)
            {
                _hostSuggestions.Add(new HostSuggestion(
                    machine.Name,
                    string.IsNullOrWhiteSpace(machine.Ip) ? "局域网发现" : $"局域网 · {machine.Ip}",
                    machine.Name,
                    null));
            }
        }
        catch (OperationCanceledException)
        {
            // The window was closed or another refresh superseded this one.
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

    private void HostBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (HostBox.SelectedItem is not HostSuggestion suggestion)
        {
            return;
        }

        if (suggestion.Config is { } config)
        {
            NameBox.Text = config.Alias;
            HostBox.Text = config.HostName;
            SshPortBox.Text = config.Port.ToString();
            UserBox.Text = config.User ?? "";
        }
        else
        {
            HostBox.Text = suggestion.Value;
        }

        HostBox.SelectedIndex = -1;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var profile = BuildProfile();
        if (profile is null)
        {
            System.Windows.MessageBox.Show(
                this,
                "请检查：服务器地址、用户名、远程地址必填，端口必须是 1–65535。",
                "Harness Portable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var password = string.IsNullOrEmpty(PasswordInput.Password) ? null : PasswordInput.Password;
        Result = new EditorResult(profile, password);
        DialogResult = true;
    }

    private TunnelProfile? BuildProfile()
    {
        if (!TryPort(SshPortBox.Text, 22, out var sshPort) ||
            !TryPort(RemotePortBox.Text, 3080, out var remotePort) ||
            !TryPort(LocalPortBox.Text, 3080, out var localPort))
        {
            return null;
        }

        var host = HostBox.Text.Trim();
        var user = UserBox.Text.Trim();
        var remoteHost = RemoteHostBox.Text.Trim();

        if (host.Length == 0 || user.Length == 0 || remoteHost.Length == 0)
        {
            return null;
        }

        var name = NameBox.Text.Trim();
        return new TunnelProfile
        {
            Id = _initial?.Id ?? Guid.NewGuid().ToString(),
            Name = name.Length == 0 ? host : name,
            SshHost = host,
            SshPort = sshPort,
            User = user,
            RemoteHost = remoteHost,
            RemotePort = remotePort,
            LocalPort = localPort,
        };
    }

    private static bool TryPort(string text, int fallback, out int port)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            port = fallback;
            return true;
        }

        return int.TryParse(trimmed, out port) && port is >= 1 and <= 65535;
    }
}
