using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using AvalonDock.Layout;

namespace HarnessPortable.Windows.Services;

/// <summary>Temporarily expands a pane in place; documents and browser hosts never move.</summary>
public sealed class WorkspacePaneFocus
{
    private readonly List<Action> _restore = [];
    private readonly Dictionary<ILayoutElement, (GridLength Width, GridLength Height)> _sizes = [];
    public FrameworkElement? Target { get; private set; }
    public bool IsActive => Target is not null;

    private static (GridLength Width, GridLength Height)? DockSizes(ILayoutElement model) => model switch
    {
        LayoutPanel panel => (panel.DockWidth, panel.DockHeight),
        LayoutDocumentPaneGroup group => (group.DockWidth, group.DockHeight),
        LayoutDocumentPane pane => (pane.DockWidth, pane.DockHeight),
        _ => null
    };
    public GridLength SavedWidth(ILayoutElement model) => _sizes.TryGetValue(model, out var size) ? size.Width : DockSizes(model)!.Value.Width;
    public GridLength SavedHeight(ILayoutElement model) => _sizes.TryGetValue(model, out var size) ? size.Height : DockSizes(model)!.Value.Height;

    public bool Maximize(FrameworkElement target, FrameworkElement boundary, IEnumerable<ILayoutElement> models)
    {
        Restore();
        var branch = new List<(Grid Grid, UIElement Child)>();
        DependencyObject current = target;
        while (!ReferenceEquals(current, boundary))
        {
            var parent = VisualTreeHelper.GetParent(current);
            if (parent is null) return false;
            if (parent is Grid grid && current is UIElement child && grid.Children.Count > 1 &&
                (grid.ColumnDefinitions.Count > 1 || grid.RowDefinitions.Count > 1)) branch.Add((grid, child));
            current = parent;
        }
        if (branch.Count == 0) return false;
        foreach (var model in models) if (DockSizes(model) is { } size) _sizes[model] = size;
        Target = target;
        foreach (var (grid, child) in branch)
        {
            foreach (UIElement sibling in grid.Children)
                if (!ReferenceEquals(sibling, child)) Override(sibling, UIElement.VisibilityProperty, Visibility.Collapsed);
            for (var i = 0; i < grid.ColumnDefinitions.Count; i++)
            {
                var column = grid.ColumnDefinitions[i];
                Override(column, ColumnDefinition.MinWidthProperty, 0d);
                Override(column, ColumnDefinition.MaxWidthProperty, double.PositiveInfinity);
                Override(column, ColumnDefinition.WidthProperty, i == Grid.GetColumn(child) ? new GridLength(1, GridUnitType.Star) : new GridLength(0));
            }
            for (var i = 0; i < grid.RowDefinitions.Count; i++)
            {
                var row = grid.RowDefinitions[i];
                Override(row, RowDefinition.MinHeightProperty, 0d);
                Override(row, RowDefinition.MaxHeightProperty, double.PositiveInfinity);
                Override(row, RowDefinition.HeightProperty, i == Grid.GetRow(child) ? new GridLength(1, GridUnitType.Star) : new GridLength(0));
            }
        }
        return true;
    }

    private void Override(DependencyObject target, DependencyProperty property, object value)
    {
        var binding = BindingOperations.GetBindingBase(target, property);
        var local = target.ReadLocalValue(property);
        _restore.Add(() =>
        {
            if (binding is not null) BindingOperations.SetBinding(target, property, binding);
            else if (local == DependencyProperty.UnsetValue) target.ClearValue(property);
            else target.SetValue(property, local);
        });
        // Replacing the local binding temporarily prevents a two-way binding
        // from saving zero widths to the real workspace model.
        target.SetValue(property, value);
    }

    public void Restore()
    {
        Target = null;
        for (var i = _restore.Count - 1; i >= 0; i--) _restore[i]();
        _restore.Clear();
        // AvalonDock can adjust absolute lengths on resize. Keep the original
        // split lengths even when the main window was resized during focus.
        foreach (var (model, size) in _sizes)
        {
            switch (model)
            {
                case LayoutPanel panel: panel.DockWidth = size.Width; panel.DockHeight = size.Height; break;
                case LayoutDocumentPaneGroup group: group.DockWidth = size.Width; group.DockHeight = size.Height; break;
                case LayoutDocumentPane pane: pane.DockWidth = size.Width; pane.DockHeight = size.Height; break;
            }
        }
        _sizes.Clear();
    }
}
