using System.Windows;
using System.Windows.Controls;
using HarnessPortable.Windows.Services;
using UserControl = System.Windows.Controls.UserControl;
using Control = System.Windows.Controls.Control;

namespace HarnessPortable.Windows.Controls;

public partial class ConfigurationSyncPanel : UserControl
{
    private ConfigurationSyncService? _sync;
    public ConfigurationSyncPanel() { InitializeComponent(); Loaded += (_, _) => { if (_sync != null) { _sync.Changed -= Refresh; _sync.Changed += Refresh; Refresh(); } }; Unloaded += (_, _) => { if (_sync != null) _sync.Changed -= Refresh; }; }
    public void Bind(ConfigurationSyncService sync) { _sync = sync; Reload(); }
    private void Reload()
    {
        if (_sync == null) return; var value = _sync.Status();
        Address.Text = value["url"]!.GetValue<string>(); Username.Text = value["username"]!.GetValue<string>();
        AllowHttp.IsChecked = value["allowHttp"]!.GetValue<bool>(); AutoSync.IsChecked = value["autoSync"]!.GetValue<bool>();
        Password.Clear(); ClearPassword.IsChecked = false; Refresh();
    }
    private void Refresh()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(Refresh); return; }
        if (_sync == null) return; var value = _sync.Status(); var busy = _sync.Busy;
        foreach (var control in new Control[] { Address, Username, Password, ClearPassword, AllowHttp, AutoSync, Save, Reset, Test, Run }) control.IsEnabled = !busy;
        Cancel.IsEnabled = busy; Status.Text = _sync.Message;
        LastSuccess.Text = value["lastSuccess"] is { } time && DateTimeOffset.TryParse(time.GetValue<string>(), out var parsed) ? "上次成功：" + parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "尚无成功同步记录";
        ConflictPanel.Visibility = _sync.Phase == "conflict" ? Visibility.Visible : Visibility.Collapsed;
        ConflictItems.Text = string.Join("\n", _sync.Conflicts.Take(15)) + (_sync.Conflicts.Length > 15 ? "\n…" : "");
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try { _sync!.Configure(new(Address.Text, Username.Text, AutoSync.IsChecked == true, AllowHttp.IsChecked == true), Password.Password.Length == 0 ? null : Password.Password, ClearPassword.IsChecked == true); Password.Clear(); ClearPassword.IsChecked = false; }
        catch (Exception) { Status.Text = "保存失败，请检查目录地址；更换地址或账号时需重填或清除同步密码。"; }
    }
    private void Reset_Click(object sender, RoutedEventArgs e) => Reload();
    private async void Test_Click(object sender, RoutedEventArgs e) => await _sync!.TestAsync();
    private async void Run_Click(object sender, RoutedEventArgs e) => await _sync!.RunAsync();
    private void Cancel_Click(object sender, RoutedEventArgs e) => _sync!.Cancel();
    private async void Local_Click(object sender, RoutedEventArgs e) => await _sync!.RunAsync("local");
    private async void Remote_Click(object sender, RoutedEventArgs e) => await _sync!.RunAsync("remote");
}
