using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HarnessPortable.Windows.Services;
using Button = System.Windows.Controls.Button;
using DataObject = System.Windows.DataObject;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using TextBoxBase = System.Windows.Controls.Primitives.TextBoxBase;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using ScrollBar = System.Windows.Controls.Primitives.ScrollBar;
using Control = System.Windows.Controls.Control;

namespace HarnessPortable.Windows.Controls;

public partial class ConnectionSidebar
{
    private const string ConnectionDragFormat = "HarnessPortable.Connection";
    private Point _dragOrigin;
    private string? _dragKey;
    private ListBoxItem? _dropMarker;

    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    private bool TryOpenBlankMenu(DependencyObject? source)
    {
        if (Ancestor<ListBoxItem>(source) is not null || Ancestor<TextBoxBase>(source) is not null ||
            Ancestor<ButtonBase>(source) is not null || Ancestor<ScrollBar>(source) is not null) return false;
        AddRequested?.Invoke();
        return true;
    }

    private void Blank_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (TryOpenBlankMenu(e.OriginalSource as DependencyObject)) e.Handled = true;
    }

    private void Drag_Pressed(object sender, MouseButtonEventArgs e)
    {
        _dragKey = null;
        if (_filter != ConnectionLibraryFilter.Recent &&
            Ancestor<Button>(e.OriginalSource as DependencyObject) is { Name: "OpenConnectionButton", Tag: ConnectionRow row })
        {
            _dragKey = row.Entry.Key;
            _dragOrigin = e.GetPosition(ConnectionList);
        }
    }
    private void Drag_Released(object sender, MouseButtonEventArgs e) => _dragKey = null;
    private void Drag_Moved(object sender, MouseEventArgs e)
    {
        if (_dragKey is null || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(ConnectionList);
        if (Math.Abs(position.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var key = _dragKey;
        _dragKey = null;
        Mouse.Capture(null); // Cancel the button click when this becomes a drag.
        try { DragDrop.DoDragDrop(ConnectionList, new DataObject(ConnectionDragFormat, key), DragDropEffects.Move); }
        finally { ClearDropMarker(); }
        e.Handled = true;
    }

    private (ConnectionRow Row, ListBoxItem Container, bool After)? DropTarget(DragEventArgs e)
    {
        if (_filter == ConnectionLibraryFilter.Recent || !e.Data.GetDataPresent(ConnectionDragFormat)) return null;
        var key = e.Data.GetData(ConnectionDragFormat) as string;
        if (key is null || !ConnectionList.Items.OfType<ConnectionRow>().Any(row => row.Entry.Key == key)) return null;
        var container = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is not ConnectionRow row || row.Entry.Key == key) return null;
        return (row, container, e.GetPosition(container).Y >= container.ActualHeight / 2);
    }
    private void Connection_DragOver(object sender, DragEventArgs e)
    {
        ClearDropMarker();
        e.Effects = DragDropEffects.None;
        if (DropTarget(e) is { } target)
        {
            _dropMarker = target.Container;
            _dropMarker.BorderThickness = target.After ? new Thickness(0, 0, 0, 2) : new Thickness(0, 2, 0, 0);
            e.Effects = DragDropEffects.Move;
            var scroll = FindScrollViewer(ConnectionList);
            var y = e.GetPosition(ConnectionList).Y;
            if (y < 24) scroll?.LineUp();
            else if (y > ConnectionList.ActualHeight - 24) scroll?.LineDown();
        }
        e.Handled = true;
    }
    private void Connection_Drop(object sender, DragEventArgs e)
    {
        var target = DropTarget(e);
        ClearDropMarker();
        e.Effects = DragDropEffects.None;
        if (target is { } t && e.Data.GetData(ConnectionDragFormat) is string key &&
            _library?.Move(key, t.Row.Entry.Key, t.After, _filter == ConnectionLibraryFilter.All) == true)
        {
            e.Effects = DragDropEffects.Move;
            Refresh();
        }
        e.Handled = true;
    }
    private void Connection_DragLeave(object sender, DragEventArgs e) => ClearDropMarker();
    private void ClearDropMarker()
    {
        _dropMarker?.ClearValue(Control.BorderThicknessProperty);
        _dropMarker = null;
    }
}
