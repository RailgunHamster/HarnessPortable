using System.Windows;
using System.Windows.Controls;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows;

public partial class DirectConnectionWindow : Window
{
    public string Url { get; private set; } = "";
    public string AuthMode { get; private set; } = "none";
    public ConnectionPreference Preference { get; private set; } = new();
    public bool OpenAfterSave { get; private set; }
    public DirectConnectionWindow(string? url = null, ConnectionPreference? preference = null, string authMode = "none")
    {
        InitializeComponent(); AppTheme.Track(this);
        UrlBox.Text = url ?? ""; NameBox.Text = preference?.Name ?? ""; GroupBox.Text = preference?.Group ?? "";
        FavoriteBox.IsChecked = preference?.Favorite == true;
        AuthBox.SelectedIndex = authMode == "nssm" ? 1 : 0;
        Hint.Text = "修改仅影响此连接，下次打开网页生效。";
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var url = ProfileStore.NormalizeUrl(UrlBox.Text);
        if (url is null) { Hint.Text = "请输入有效的 HTTP(S) 地址、IP 或机器名。"; return; }
        Url = url; AuthMode = (AuthBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "none";
        Preference = new() { Name = NameBox.Text.Trim(), Group = GroupBox.Text.Trim(), Favorite = FavoriteBox.IsChecked == true };
        OpenAfterSave = (sender as FrameworkElement)?.Tag as string == "connect";
        DialogResult = true;
    }
}
