using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HarnessPortable.Windows;
using HarnessPortable.Windows.Controls;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

// TEMPORARY probe: renders the management page in both token sets, with the
// update progress bar forced visible, so the progress UI can be looked at.
public sealed class TempProgressRenderProbe
{
    [Fact]
    public void RenderProgressInBothThemes()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "hp-render");
        Directory.CreateDirectory(outDir);
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                var app = new System.Windows.Application();
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/HarnessPortable;component/Themes/Modern.xaml"),
                });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/HarnessPortable;component/Themes/DshManagement.xaml"),
                });

                var services = new AppServices();
                var view = new ManagementView(services);
                var window = new ManagementWindow(view) { Width = 1020, Height = 900 };
                window.Show();
                window.UpdateLayout();

                if (view.FindName("UpdateProgressBar") is ProgressBar bar)
                {
                    bar.Visibility = Visibility.Visible;
                    bar.Value = 62;
                }

                if (view.FindName("UpdateStatusText") is TextBlock status)
                {
                    status.Text = "正在下载更新… 62%";
                }

                if (view.FindName("ApplyUpdateButton") is Button apply)
                {
                    apply.Content = "下载 2.6.10 并重启";
                }

                foreach (var (dark, name) in new[] { (false, "light"), (true, "dark") })
                {
                    AppTheme.Apply(dark);
                    window.UpdateLayout();
                    view.UpdateLayout();

                    if (view.Content is DockPanel dock &&
                        dock.Children.OfType<ScrollViewer>().FirstOrDefault()?.Content is FrameworkElement content)
                    {
                        content.UpdateLayout();
                        Render(content, Path.Combine(outDir, $"progress-{name}.png"), 2);
                    }

                    if (view.FindName("UpdateProgressBar") is ProgressBar current)
                    {
                        Render(current, Path.Combine(outDir, $"progressbar-{name}.png"), 6);
                    }
                }

                window.Close();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(90));

        Assert.Null(failure);
    }

    private static void Render(FrameworkElement element, string path, int scale)
    {
        var width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth) * scale);
        var height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight) * scale);
        var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);

        Console.WriteLine($"RENDERED {path} {width}x{height}");
    }
}
