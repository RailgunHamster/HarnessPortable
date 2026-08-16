using System.Windows;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows;

public partial class App : System.Windows.Application
{
    private AppServices _services = null!;
    private TrayIconService? _tray;

    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _services = new AppServices();

        var main = new MainWindow(_services);
        MainWindow = main;

        _tray = new TrayIconService(
            showMainWindow: () => ShowMainWindow(main),
            exit: ExitApplication);

        main.Show();
    }

    private static void ShowMainWindow(MainWindow main)
    {
        if (!main.IsVisible)
        {
            main.Show();
        }

        if (main.WindowState == WindowState.Minimized)
        {
            main.WindowState = WindowState.Normal;
        }

        main.Activate();
    }

    private void ExitApplication()
    {
        IsExiting = true;
        _services.Tunnels.StopAll();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services.Tunnels.StopAll();
        _tray?.Dispose();
        base.OnExit(e);
    }
}
