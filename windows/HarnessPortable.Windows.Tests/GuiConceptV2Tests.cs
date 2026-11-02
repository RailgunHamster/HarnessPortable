using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HarnessPortable.Windows.Controls;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class GuiConceptV2Tests
{
    [Fact]
    public void TunnelTopologyPillAndSidebarBadgesRenderInLightAndDarkThemes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var tempData = Path.Combine(Path.GetTempPath(), "hp-concept-v2-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempData);
                Environment.SetEnvironmentVariable("HARNESS_PORTABLE_DATA_DIRECTORY", tempData);

                if (System.Windows.Application.Current is null)
                {
                    var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("pack://application:,,,/HarnessPortable;component/Themes/Modern.xaml", UriKind.Absolute)
                    });
                }

                var services = new AppServices();
                services.Profiles.Save(new ProfileStore.Config
                {
                    Tunnels =
                    [
                        new TunnelProfile
                        {
                            Id = "rharness-prod",
                            Name = "RHarness 混合模型工作台",
                            SshHost = "gpu-cluster.internal",
                            SshPort = 22,
                            User = "devops",
                            RemoteHost = "127.0.0.1",
                            RemotePort = 4317,
                            LocalPort = 4317
                        },
                        new TunnelProfile
                        {
                            Id = "codex-staging",
                            Name = "内网构建与制品服务",
                            SshHost = "build-node-02.lan",
                            SshPort = 2222,
                            User = "runner",
                            RemoteHost = "10.0.4.18",
                            RemotePort = 8080,
                            LocalPort = 18080
                        }
                    ],
                    Directs =
                    [
                        "https://cnb.cool/railgunhamster",
                        "http://127.0.0.1:4318/"
                    ]
                });
                var lib = new ConnectionLibrary(services.Profiles, services.Settings);
                lib.Update(ConnectionLibrary.TunnelKey("rharness-prod"), p => { p.Group = "AI 研发集群"; p.Favorite = true; });
                lib.Update(ConnectionLibrary.TunnelKey("codex-staging"), p => { p.Group = "基础设施"; });

                var outDir = Path.Combine(tempData, "renders");
                Directory.CreateDirectory(outDir);

                foreach (var (theme, dark) in new[] { ("light", false), ("dark", true) })
                {
                    AppTheme.Apply(dark);

                    var mgmt = new ManagementView(services)
                    {
                        Width = 960,
                        Height = 640
                    };
                    RenderControlToPng(mgmt, 960, 640, Path.Combine(outDir, $"harness-v2-management-{theme}.png"));

                    var sidebar = new ConnectionSidebar
                    {
                        Width = 268,
                        Height = 560
                    };
                    sidebar.Initialize(services);
                    sidebar.Refresh();
                    RenderControlToPng(sidebar, 268, 560, Path.Combine(outDir, $"harness-v2-sidebar-{theme}.png"));
                }

                var item = new ManagementView.TunnelListItem
                {
                    Profile = services.Profiles.LoadTunnels()[0],
                    StopVisibility = Visibility.Visible
                };
                Assert.Equal("127.0.0.1:4317 📋", item.LocalEndpoint);
                Assert.Contains("SSH · devops@gpu-cluster.internal:22", item.SshBridgeText);
                Assert.Equal("127.0.0.1:4317", item.RemoteEndpoint);
                Assert.Equal("打开网页", item.PrimaryActionLabel);

                try { Directory.Delete(tempData, recursive: true); } catch { }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(25)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RenderControlToPng(FrameworkElement element, int width, int height, string filePath)
    {
        element.Measure(new System.Windows.Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();

        var dpi = 144.0;
        var scale = dpi / 96.0;
        var bmp = new RenderTargetBitmap(
            (int)Math.Round(width * scale),
            (int)Math.Round(height * scale),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bmp.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(filePath);
        encoder.Save(fs);
    }
}
