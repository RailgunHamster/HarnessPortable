using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using HarnessPortable.Windows.Services;
using ComboBox = System.Windows.Controls.ComboBox;

namespace HarnessPortable.Windows;

public partial class SettingsWindow : Window
{
    private readonly AppServices _services;
    private UpdateSelection _draft = new();
    private bool _loading = true;
    public event Action? LibraryRequested;

    public SettingsWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        AppTheme.Track(this);
        var settings = services.Settings.Load();
        SelectTag(AppearanceBox, settings.Appearance);
        SelectTag(CloseBox, settings.CloseBehavior);
        RestoreBox.IsChecked = settings.RestoreLastLayoutOnStartup;
        ReconnectBox.IsChecked = settings.ReconnectRestoredWorkspace;
        ResetDraft();
        VersionLabel.Text = $"Harness Portable · {services.Updates.CurrentVersion} · {services.Updates.DeploymentLabel}";
        Loaded += (_, _) => { services.Updates.StateChanged += RefreshUpdates; RefreshUpdates(); };
        Closed += (_, _) => services.Updates.StateChanged -= RefreshUpdates;
        _loading = false;
    }

    private static void SelectTag(ComboBox box, string tag) => box.SelectedItem = box.Items.OfType<ComboBoxItem>()
        .FirstOrDefault(i => i.Tag as string == tag) ?? box.Items[0];
    private static string SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
    private void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (GeneralPage is null) return;
        var pages = new[] { GeneralPage, WorkspacePage, SecurityPage, UpdatesPage };
        for (var i = 0; i < pages.Length; i++) pages[i].Visibility = i == Categories.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Preference_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var settings = _services.Settings.Load();
        settings.Appearance = SelectedTag(AppearanceBox);
        settings.CloseBehavior = SelectedTag(CloseBox);
        settings.RestoreLastLayoutOnStartup = RestoreBox.IsChecked == true;
        settings.ReconnectRestoredWorkspace = ReconnectBox.IsChecked == true;
        _services.Settings.Save(settings);
        AppTheme.SetPreference(settings.Appearance);
        SaveStatus.Text = "已保存 · 本设备偏好立即生效";
    }
    private void ResetDraft()
    {
        _loading = true;
        _draft = UpdateSelection.From(_services.Settings.Load());
        SelectTag(SourceBox, _draft.Slot == UpdateSourceSlot.GitHub ? "github" : "home");
        SourceAddressBox.Text = _draft.Url();
        DraftHint.Text = "修改后需要保存；检查更新使用已保存的源。";
        _loading = false;
    }
    private UpdateSelection TypedDraft() => new()
    {
        Slot = _draft.Slot,
        HomeUrl = _draft.Slot == UpdateSourceSlot.Home ? SourceAddressBox.Text.Trim() : _draft.HomeUrl,
        GitHubUrl = _draft.Slot == UpdateSourceSlot.GitHub ? SourceAddressBox.Text.Trim() : _draft.GitHubUrl,
    };
    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var typed = TypedDraft();
        _draft = new() { Slot = SelectedTag(SourceBox) == "github" ? UpdateSourceSlot.GitHub : UpdateSourceSlot.Home, HomeUrl = typed.HomeUrl, GitHubUrl = typed.GitHubUrl };
        SourceAddressBox.Text = _draft.Url();
        DraftHint.Text = "有未保存的修改 · 切换分类或检查更新不会保存草稿";
    }
    private void SourceAddress_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loading) DraftHint.Text = "有未保存的修改 · 请点“保存并使用”";
    }
    private void SaveSource_Click(object sender, RoutedEventArgs e)
    {
        if (_services.Updates.Busy) { SaveStatus.Text = "检查或下载结束后再保存更新源"; return; }
        var next = TypedDraft();
        if (!UpdateSourceFactory.IsValid(next.Url(), next.Slot == UpdateSourceSlot.GitHub))
        {
            DraftHint.Text = "请输入完整的 GitHub 仓库地址、HTTP(S) 地址或绝对目录路径。";
            return;
        }
        var settings = _services.Settings.Load(); next.Store(settings); _services.Settings.Save(settings);
        _services.Updates.ResetSourceResult();
        ResetDraft(); RefreshUpdates(); SaveStatus.Text = "已保存更新源 · 下次检查使用此地址";
    }
    private void ResetSource_Click(object sender, RoutedEventArgs e) { ResetDraft(); SaveStatus.Text = "已撤销更新源修改"; }
    private void OfficialSource_Click(object sender, RoutedEventArgs e)
    {
        if (_services.Updates.Busy) return;
        var stored = UpdateSelection.From(_services.Settings.Load());
        var settings = _services.Settings.Load();
        new UpdateSelection { Slot = UpdateSourceSlot.GitHub, HomeUrl = stored.HomeUrl, GitHubUrl = UpdateSelection.GitHub }.Store(settings);
        _services.Settings.Save(settings); _services.Updates.ResetSourceResult(); ResetDraft(); RefreshUpdates(); SaveStatus.Text = "已保存 · 使用官方 GitHub 更新源";
    }
    private async void Check_Click(object sender, RoutedEventArgs e) => await _services.Updates.CheckAsync(UpdateSelection.From(_services.Settings.Load()).Url());
    private async void Install_Click(object sender, RoutedEventArgs e) => await _services.Updates.ApplyAsync(UpdateSelection.From(_services.Settings.Load()).Url());
    private void RefreshUpdates()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(RefreshUpdates); return; }
        var updates = _services.Updates;
        CurrentSourceLabel.Text = "正在使用：" + UpdateSelection.From(_services.Settings.Load()).Label(UpdateSelection.From(_services.Settings.Load()).Slot);
        UpdateStatusLabel.Text = updates.Status; ReleaseNotes.Text = updates.ChangelogText;
        CheckButton.IsEnabled = !updates.Busy;
        InstallButton.Visibility = updates.CanApply ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.IsEnabled = !updates.Busy;
        InstallButton.Content = $"下载 {updates.AvailableVersion} 并重启";
        Progress.Value = updates.Progress; Progress.Visibility = updates.Busy ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ReturnToLibrary_Click(object sender, RoutedEventArgs e) { LibraryRequested?.Invoke(); Close(); }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "连接配置 (*.json)|*.json", FileName = "harness-connections.json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_services.Profiles.Load(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
            SaveStatus.Text = "连接配置已导出 · 不包含密码、私钥内容和登录令牌";
        }
        catch (Exception error) { SaveStatus.Text = "导出失败：" + error.Message; }
    }
}
