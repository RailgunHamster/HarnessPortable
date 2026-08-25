using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace HarnessPortable.Windows;

public partial class TunnelEditorWindow : Window
{
    public sealed record EditorResult(
        TunnelProfile Profile,
        string? Password,
        string? SshConfigPath);

    private sealed record HostSuggestion(
        string Title,
        string Detail,
        string Value,
        SshConfigHost? Config);

    private readonly TunnelProfile? _initial;
    private readonly ObservableCollection<HostSuggestion> _hostSuggestions = [];
    private CancellationTokenSource? _configLoadCts;
    private string? _sshConfigPath;

    public EditorResult? Result { get; private set; }

    public TunnelEditorWindow(TunnelProfile? initial, string? sshConfigPath = null)
    {
        _initial = initial;
        _sshConfigPath = string.IsNullOrWhiteSpace(sshConfigPath) ? null : sshConfigPath;
        InitializeComponent();
        HostBox.ItemsSource = _hostSuggestions;
        UpdateSshConfigPathText(SshConfigReader.ResolvePath(_sshConfigPath));
        Closed += (_, _) => _configLoadCts?.Cancel();

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
        await LoadSshConfigAsync();
    }

    private async void RefreshSshConfig_Click(object sender, RoutedEventArgs e)
    {
        await LoadSshConfigAsync();
        HostBox.IsDropDownOpen = true;
    }

    private async void ChooseSshConfig_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new WpfOpenFileDialog
        {
            Title = "选择 SSH config 文件",
            Filter = "SSH config|config|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        var currentPath = SshConfigReader.ResolvePath(_sshConfigPath);
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            dialog.InitialDirectory = Path.GetDirectoryName(currentPath);
            dialog.FileName = Path.GetFileName(currentPath);
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _sshConfigPath = dialog.FileName;
        await LoadSshConfigAsync();
        HostBox.IsDropDownOpen = true;
    }

    private async Task LoadSshConfigAsync()
    {
        _configLoadCts?.Cancel();
        _configLoadCts?.Dispose();
        var cts = new CancellationTokenSource();
        _configLoadCts = cts;
        var selectedConfigPath = _sshConfigPath;

        try
        {
            var config = await Task.Run(() => SshConfigReader.Load(selectedConfigPath), cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            UpdateSshConfigPathText(config.Path);
            var text = HostBox.Text;
            _hostSuggestions.Clear();
            foreach (var configHost in config.Hosts)
            {
                _hostSuggestions.Add(new HostSuggestion(
                    configHost.Alias,
                    $"SSH 配置 · {configHost.ConnectionLabel}",
                    configHost.HostName,
                    configHost));
            }

            HostBox.SelectedIndex = -1;
            HostBox.Text = text;

        }
        catch (OperationCanceledException)
        {
            // The window was closed or another refresh superseded this one.
        }
        finally
        {
            if (ReferenceEquals(_configLoadCts, cts))
            {
                _configLoadCts = null;
            }

            cts.Dispose();
        }
    }

    private void UpdateSshConfigPathText(string? path)
    {
        SshConfigPathText.Text = string.IsNullOrWhiteSpace(path)
            ? "SSH 配置：未找到 ~/.ssh/config"
            : $"SSH 配置：{path}";
    }

    private void HostBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (HostBox.SelectedItem is not HostSuggestion suggestion)
        {
            return;
        }

        var hostValue = suggestion.Value;
        if (suggestion.Config is { } config)
        {
            NameBox.Text = config.Alias;
            hostValue = config.HostName;
            SshPortBox.Text = config.Port.ToString();
            UserBox.Text = config.User ?? "";
        }

        // Keep the selected item while WPF finishes the ComboBox selection
        // transaction; clearing SelectedIndex here would restore the old text.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.DataBind,
            new Action(() => HostBox.Text = hostValue));
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
        Result = new EditorResult(profile, password, _sshConfigPath);
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
