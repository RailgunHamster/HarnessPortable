using System.IO;
using System.Text;
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
        AppPaths.Ensure();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _services = new AppServices();

            var main = new MainWindow(_services);
            MainWindow = main;

            _tray = new TrayIconService(
                showMainWindow: () => ShowMainWindow(main),
                exit: ExitApplication);

            main.Show();
        }
        catch (Exception ex)
        {
            WriteCrash("startup", ex);
            MessageBox.Show(
                $"Harness Portable 启动失败。\n\n{ex.Message}\n\n详细信息已写入：\n{AppPaths.CrashLogFile}",
                "Harness Portable",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrash("dispatcher", e.Exception);
        // Let the normal WPF crash path finish after recording the exception.
        e.Handled = false;
    }

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            WriteCrash("app-domain", ex);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrash("task", e.Exception);
        e.SetObserved();
    }

    private static void WriteCrash(string source, Exception exception)
    {
        try
        {
            AppPaths.Ensure();
            var text = new StringBuilder()
                .AppendLine($"[{DateTimeOffset.Now:O}] {source}")
                .AppendLine(exception.ToString())
                .AppendLine(new string('-', 80))
                .ToString();
            File.AppendAllText(AppPaths.CrashLogFile, text);
        }
        catch
        {
            // Crash logging must never create a second failure.
        }
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
