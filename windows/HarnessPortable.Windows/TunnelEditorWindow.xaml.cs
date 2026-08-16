using System.Windows;
using HarnessPortable.Windows.Models;

namespace HarnessPortable.Windows;

public partial class TunnelEditorWindow : Window
{
    public sealed record EditorResult(TunnelProfile Profile, string? Password);

    private readonly TunnelProfile? _initial;

    public EditorResult? Result { get; private set; }

    public TunnelEditorWindow(TunnelProfile? initial)
    {
        _initial = initial;
        InitializeComponent();

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
