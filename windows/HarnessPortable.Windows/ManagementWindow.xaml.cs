using System.Windows;
using HarnessPortable.Windows.Controls;

namespace HarnessPortable.Windows;

/// <summary>
/// Hosts the management UI in its own top-level window. The panel used to be a
/// dock tab, which meant a workspace/dock problem could make it unreachable —
/// and the workspace window itself can be closed to the tray.
///
/// Closing the window only closes the window: the panel object is reused, so the
/// next open comes back with the same state. The application does not exit here,
/// and while the app is shutting down nothing is cancelled (ShutdownMode is
/// OnMainWindowClose, so this window closes along with it).
/// </summary>
public partial class ManagementWindow : Window
{
    public ManagementWindow(ManagementView view)
    {
        InitializeComponent();
        ApplyIcon();
        Host.Content = view;
    }

    /// <summary>
    /// Set in code rather than in XAML on purpose: a leading-slash pack URI
    /// resolves against the entry assembly, and a missing icon resource must
    /// never be the reason the management UI cannot be opened.
    /// </summary>
    private void ApplyIcon()
    {
        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(
                "pack://application:,,,/HarnessPortable;component/Assets/HarnessPortable.png",
                UriKind.Absolute));
        }
        catch
        {
            // Cosmetic only.
        }
    }
}
