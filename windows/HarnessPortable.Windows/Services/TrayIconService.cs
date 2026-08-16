using System.Drawing;
using System.Windows.Forms;

namespace HarnessPortable.Windows.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Action _showMainWindow;
    private readonly Action _exit;

    public TrayIconService(Action showMainWindow, Action exit)
    {
        _showMainWindow = showMainWindow;
        _exit = exit;

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Harness Portable", null, (_, _) => _showMainWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => _exit());

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Harness Portable",
            Visible = true,
            ContextMenuStrip = menu,
        };

        _icon.DoubleClick += (_, _) => _showMainWindow();
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(2000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
