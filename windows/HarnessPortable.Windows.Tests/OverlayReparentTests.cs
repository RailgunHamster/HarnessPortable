using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HarnessPortable.Windows.Tests;

/// <summary>
/// Reproduces the 2.6.23 drag crash shape: a Canvas declared in a main
/// window's Grid gets moved into a separate transparent overlay window. WPF
/// never auto-removes a child from a Panel, so the move must detach it first
/// — otherwise the overlay's Show() throws ArgumentException ("the specified
/// child is already a child of another Visual").
/// </summary>
public class OverlayReparentTests
{
    [Fact]
    public void CanvasMovedToOverlayWindow_ShowsWithoutReparentCrash()
    {
        Exception? failure = null;
        var done = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            try
            {
                var canvas = new Canvas
                {
                    Background = Brushes.Transparent,
                    Width = 100,
                    Height = 100,
                };
                var grid = new Grid();
                grid.Children.Add(canvas);

                var main = new Window { Content = grid, Width = 200, Height = 200 };
                var overlay = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                    Width = 200,
                    Height = 200,
                };

                // The detach MainWindow.InitializeTabDragOverlayWindow now performs.
                if (canvas.Parent is Panel panel)
                {
                    panel.Children.Remove(canvas);
                }

                overlay.Content = canvas;

                // Would have thrown before the detach.
                overlay.Show();
                overlay.Close();
                main.Close();

                if (!ReferenceEquals(overlay, canvas.Parent))
                {
                    throw new InvalidOperationException("canvas not parented to the overlay window");
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                done.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(done.Wait(TimeSpan.FromSeconds(30)), "STA test thread did not finish");
        Assert.Null(failure);
    }
}
