using Velopack;

namespace HarnessPortable.Windows;

/// <summary>
/// Custom entry point so Velopack can handle install/update/uninstall hooks
/// before WPF starts. Must be the first code that runs.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
