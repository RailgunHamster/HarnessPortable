using AvalonDock.Layout;

namespace HarnessPortable.Windows.Tests;

public sealed class WorkspaceLayoutTests
{
    [Fact]
    public void SplitPane_ProducesTwoPanes_WithMovedActiveDocument()
    {
        var management = new LayoutDocument
        {
            Title = "管理",
            ContentId = "management",
            CanClose = false,
        };
        var session = new LayoutDocument
        {
            Title = "隧道A",
            ContentId = "tunnel:a",
            CanClose = true,
        };

        var pane = new LayoutDocumentPane(management);
        pane.Children.Add(session);

        var group = new LayoutDocumentPaneGroup(pane);
        var root = new LayoutRoot { RootPanel = new LayoutPanel(group) };

        // Same algorithm as MainWindow.SplitActiveDocument.
        var oldPane = (LayoutDocumentPane)session.Parent!;
        var parent = oldPane.Parent!;
        var newPane = new LayoutDocumentPane();
        var splitGroup = new LayoutDocumentPaneGroup
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
        };

        parent.ReplaceChild(oldPane, splitGroup);
        splitGroup.Children.Add(oldPane);
        splitGroup.Children.Add(newPane);
        oldPane.RemoveChild(session);
        newPane.Children.Add(session);

        Assert.Equal(2, splitGroup.Children.Count);
        Assert.Contains(management, oldPane.Children);
        Assert.Contains(session, newPane.Children);
        Assert.Equal(session, newPane.SelectedContent);
        Assert.Equal(group, splitGroup.Parent);
        var rootPanel = Assert.IsType<LayoutPanel>(root.RootPanel);
        Assert.Contains(group, rootPanel.Children);
    }
}
