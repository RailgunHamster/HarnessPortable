using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AvalonDock.Layout;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class WorkspacePaneFocusTests
{
    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception error) { failure = error; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static Grid Split(bool vertical, FrameworkElement first, FrameworkElement second)
    {
        var grid = new Grid();
        if (vertical)
        {
            grid.RowDefinitions.Add(new() { Height = new(2, GridUnitType.Star), MinHeight = 45 });
            grid.RowDefinitions.Add(new() { Height = new(6) });
            grid.RowDefinitions.Add(new() { Height = new(3, GridUnitType.Star), MinHeight = 55 });
            Grid.SetRow(second, 2);
        }
        else
        {
            grid.ColumnDefinitions.Add(new() { Width = new(2, GridUnitType.Star), MinWidth = 90 });
            grid.ColumnDefinitions.Add(new() { Width = new(6) });
            grid.ColumnDefinitions.Add(new() { Width = new(3, GridUnitType.Star), MinWidth = 100 });
            Grid.SetColumn(second, 2);
        }
        grid.Children.Add(first); grid.Children.Add(new Border()); grid.Children.Add(second);
        if (vertical) Grid.SetRow(grid.Children[1], 1); else Grid.SetColumn(grid.Children[1], 1);
        return grid;
    }
    private static void Arrange(Grid grid, double width = 1000, double height = 700)
    {
        grid.Measure(new(width, height)); grid.Arrange(new Rect(0, 0, width, height)); grid.UpdateLayout();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HalfPaneFillsWorkspaceAndRestoresLengthsAndMinimums(bool vertical) => Sta(() =>
    {
        var first = new Border(); var second = new Border(); var grid = Split(vertical, first, second);
        var focus = new WorkspacePaneFocus(); Arrange(grid);
        Assert.True(focus.Maximize(second, grid, [])); Arrange(grid);
        Assert.Equal(Visibility.Collapsed, first.Visibility);
        Assert.Equal(1000, second.ActualWidth, 6); Assert.Equal(700, second.ActualHeight, 6);
        Assert.Same(grid, second.Parent);
        focus.Restore(); Arrange(grid);
        Assert.Equal(Visibility.Visible, first.Visibility);
        Assert.True(first.ActualWidth > 0 && first.ActualHeight > 0);
        if (vertical) { Assert.Equal(new GridLength(2, GridUnitType.Star), grid.RowDefinitions[0].Height); Assert.Equal(55, grid.RowDefinitions[2].MinHeight); }
        else { Assert.Equal(new GridLength(3, GridUnitType.Star), grid.ColumnDefinitions[2].Width); Assert.Equal(90, grid.ColumnDefinitions[0].MinWidth); }
    });

    [Fact]
    public void QuarterPaneKeepsTabContentParentsAndRestoresAfterWindowResize() => Sta(() =>
    {
        var tabs = Enumerable.Range(0, 4).Select(i => new ContentControl { Content = new Border { Tag = i } }).ToArray();
        var left = Split(true, tabs[0], tabs[1]); var right = Split(true, tabs[2], tabs[3]);
        var root = Split(false, left, right); var focus = new WorkspacePaneFocus();
        Arrange(root); var original = tabs[3].ActualHeight / right.ActualHeight;
        Assert.True(focus.Maximize(tabs[3], root, [])); Arrange(root, 1300, 900);
        Assert.Equal(1300, tabs[3].ActualWidth, 6); Assert.Equal(900, tabs[3].ActualHeight, 6);
        Assert.Same(right, tabs[3].Parent); Assert.Equal(Visibility.Collapsed, left.Visibility);
        focus.Restore(); Arrange(root, 1300, 900);
        Assert.InRange(tabs[3].ActualHeight / right.ActualHeight, original - .003, original + .003);
        Assert.All(tabs, tab => Assert.NotNull(tab.Content));
        Assert.Equal(new GridLength(3, GridUnitType.Star), root.ColumnDefinitions[2].Width);
        Assert.Equal(new GridLength(2, GridUnitType.Star), right.RowDefinitions[0].Height);
    });

    [Fact]
    public void TwoWayWidthsAndExistingHiddenChildrenAreNotOverwritten() => Sta(() =>
    {
        var first = new Border { Visibility = Visibility.Hidden }; var second = new Border(); var root = Split(false, first, second);
        var model = new LayoutDocumentPane { DockWidth = new(7, GridUnitType.Star) };
        BindingOperations.SetBinding(root.ColumnDefinitions[0], ColumnDefinition.WidthProperty, new Binding(nameof(model.DockWidth)) { Source = model, Mode = BindingMode.TwoWay });
        var focus = new WorkspacePaneFocus(); Arrange(root);
        Assert.True(focus.Maximize(second, root, [model])); Arrange(root);
        Assert.Equal(new GridLength(7, GridUnitType.Star), model.DockWidth);
        focus.Restore(); Arrange(root);
        Assert.Equal(Visibility.Hidden, first.Visibility);
        Assert.NotNull(BindingOperations.GetBindingBase(root.ColumnDefinitions[0], ColumnDefinition.WidthProperty));
        Assert.Equal(new GridLength(7, GridUnitType.Star), root.ColumnDefinitions[0].Width);
    });

    [Fact]
    public void SnapshotUsesOriginalAbsoluteLengthsDuringTemporaryResize() => Sta(() =>
    {
        var first = new Border(); var second = new Border(); var root = Split(false, first, second);
        var model = new LayoutDocumentPane { DockWidth = new(320), DockHeight = new(4, GridUnitType.Star) };
        var focus = new WorkspacePaneFocus();
        Assert.True(focus.Maximize(second, root, [model]));
        model.DockWidth = new(900); model.DockHeight = new(800);
        Assert.Equal(new GridLength(320), focus.SavedWidth(model));
        Assert.Equal(new GridLength(4, GridUnitType.Star), focus.SavedHeight(model));
        focus.Restore();
        Assert.Equal(new GridLength(320), model.DockWidth);
        Assert.Equal(new GridLength(4, GridUnitType.Star), model.DockHeight);
    });

    [Fact]
    public void UnrelatedTargetCannotHideWorkspace() => Sta(() =>
    {
        var root = Split(false, new Border(), new Border()); var focus = new WorkspacePaneFocus();
        Assert.False(focus.Maximize(new Border(), root, []));
        Assert.False(focus.IsActive);
        Assert.All(root.Children.Cast<UIElement>(), child => Assert.Equal(Visibility.Visible, child.Visibility));
    });
}
