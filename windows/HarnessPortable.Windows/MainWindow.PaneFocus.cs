using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock.Controls;
using AvalonDock.Layout;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows;

public partial class MainWindow
{
    private readonly WorkspacePaneFocus _paneFocus = new();
    private LayoutDocumentPane? _maximizedPane;
    public static readonly DependencyProperty IsPaneMaximizedProperty = DependencyProperty.Register(
        nameof(IsPaneMaximized), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public bool IsPaneMaximized { get => (bool)GetValue(IsPaneMaximizedProperty); private set => SetValue(IsPaneMaximizedProperty, value); }

    private void TogglePaneFocus_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (IsPaneMaximized) { RestorePaneFocus(); return; }
        DependencyObject? current = sender as DependencyObject;
        while (current is not null && current is not LayoutDocumentPaneControl) current = VisualTreeHelper.GetParent(current);
        if (current is not LayoutDocumentPaneControl paneControl || paneControl.Model is not LayoutDocumentPane pane) return;
        if (pane.SelectedContent is LayoutDocument selected) selected.IsActive = true;
        if (!_paneFocus.Maximize(paneControl, DockManager, DockManager.Layout.Descendents())) return;
        _maximizedPane = pane;
        IsPaneMaximized = true;
        UpdatePaneFocusPresentation();
        StatusText.Text = "当前窗格已最大化 · 点击还原图标或按 Esc 恢复分屏";
    }

    private void RestorePaneFocus()
    {
        if (!_paneFocus.IsActive) return;
        _maximizedPane = null;
        IsPaneMaximized = false;
        _paneFocus.Restore();
        UpdatePaneFocusPresentation();
        StatusText.Text = "已还原原来的分屏布局";
    }

    private void UpdatePaneFocusPresentation()
    {
        foreach (var view in AllSessionDocs().Select(d => d.Content).OfType<Controls.SessionView>()) view.PaneMaximized = IsPaneMaximized;
    }

    private void ValidatePaneFocus()
    {
        if (_maximizedPane is { } pane && (pane.Root != DockManager.Layout || pane.Children.Count == 0)) RestorePaneFocus();
    }
}
