using System.Drawing;
using System.Windows.Forms;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Tray icon for the operations that must stay reachable when the main window is
/// closed, minimized, or — as the 2.6.5 tab regression showed — not usable at
/// all. The update entry therefore never goes through the management view: it
/// talks to <see cref="UpdateService"/> directly and reports through balloon
/// tips, so a broken window can no longer lock the user out of updating.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly AppServices _services;
    private readonly NotifyIcon _icon;
    private readonly Action _showMainWindow;
    private readonly Action _showManagement;
    private readonly Action _exit;
    private readonly ToolStripMenuItem _versionItem;
    private readonly ToolStripMenuItem _updateItem;
    private bool _updateRunning;

    public TrayIconService(AppServices services, Action showMainWindow, Action showManagement, Action exit)
    {
        _services = services;
        _showMainWindow = showMainWindow;
        _showManagement = showManagement;
        _exit = exit;

        _versionItem = new ToolStripMenuItem { Enabled = false };
        _updateItem = new ToolStripMenuItem("检测并更新");
        _updateItem.Click += OnCheckAndUpdate;

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Harness Portable", null, (_, _) => _showMainWindow());
        menu.Items.Add("管理 / 设置", null, (_, _) => _showManagement());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_versionItem);
        menu.Items.Add(_updateItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => _exit());

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Harness Portable",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => _showMainWindow();

        _services.Updates.StateChanged += OnUpdateStateChanged;
        RefreshUpdateItems();
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/HarnessPortable.ico"));
            if (info?.Stream is not null)
            {
                return new Icon(info.Stream);
            }
        }
        catch
        {
            // Fall back to the generic application icon.
        }

        return SystemIcons.Application;
    }

    private void OnUpdateStateChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RefreshUpdateItems();
        }
        else
        {
            dispatcher.BeginInvoke(RefreshUpdateItems);
        }
    }

    /// <summary>
    /// Keeps the two entries in step with the update state: the version line
    /// doubles as the deployment label (which is exactly what decides whether an
    /// in-app update is possible at all), and the action becomes "install X" once
    /// a newer release is known.
    /// </summary>
    private void RefreshUpdateItems()
    {
        var updates = _services.Updates;
        _versionItem.Text = $"当前版本 {updates.CurrentVersion}（{updates.DeploymentLabel}）";
        _updateItem.Enabled = !updates.Busy && !_updateRunning;
        _updateItem.Text = updates.Busy
            ? updates.Status
            : updates.CanApply && updates.AvailableVersion is { } available
                ? $"安装 {available} 并重启"
                : "检测并更新";
    }

    private async void OnCheckAndUpdate(object? sender, EventArgs e)
    {
        if (_updateRunning)
        {
            return;
        }

        _updateRunning = true;
        RefreshUpdateItems();

        try
        {
            var url = UpdateSelection.From(_services.Settings.Load()).Url();

            if (!_services.Updates.CanApply)
            {
                await _services.Updates.CheckAsync(url);

                if (!_services.Updates.CanApply)
                {
                    // Nothing to install: the status explains why (already newest,
                    // this build cannot self-update, or the source failed).
                    ShowBalloon("检查更新", _services.Updates.Status, 8000);
                    return;
                }

                ShowBalloon(
                    $"发现新版本 {_services.Updates.AvailableVersion}",
                    "正在下载并安装，完成后会自动重启。",
                    8000);
            }
            else
            {
                ShowBalloon(
                    $"正在安装 {_services.Updates.AvailableVersion}",
                    "下载完成后会自动重启。",
                    8000);
            }

            await _services.Updates.ApplyAsync(url);

            // A successful apply restarts the app, so reaching this line means it
            // did not happen; surface whatever the service reported.
            ShowBalloon("更新未完成", _services.Updates.Status, 8000);
        }
        catch (Exception ex)
        {
            ShowBalloon("检查更新失败", ex.Message, 8000);
        }
        finally
        {
            _updateRunning = false;
            RefreshUpdateItems();
        }
    }

    public void ShowBalloon(string title, string text, int timeoutMs = 4000)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.BalloonTipIcon = ToolTipIcon.Info;
        _icon.ShowBalloonTip(timeoutMs);
    }

    public void Dispose()
    {
        _services.Updates.StateChanged -= OnUpdateStateChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }
}
