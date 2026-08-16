namespace HarnessPortable.Windows.Models;

public sealed class WorkspaceLayout
{
    public string Name { get; set; } = "";
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    public LayoutNode Root { get; set; } = new();
}

public sealed class LayoutNode
{
    /// <summary>"pane" or "split".</summary>
    public string Kind { get; set; } = "pane";

    /// <summary>"horizontal" or "vertical" for split nodes.</summary>
    public string? Orientation { get; set; }

    public List<LayoutNode> Children { get; set; } = [];

    /// <summary>Tab references for pane nodes, in left-to-right tab order.</summary>
    public List<LayoutTabRef> Tabs { get; set; } = [];

    /// <summary>Serialized AvalonDock DockWidth, e.g. "1|Star" or "320|Pixel".</summary>
    public string? DockWidth { get; set; }

    /// <summary>Serialized AvalonDock DockHeight, e.g. "1|Star" or "240|Pixel".</summary>
    public string? DockHeight { get; set; }
}

public sealed class LayoutTabRef
{
    /// <summary>"tunnel" or "direct".</summary>
    public string Kind { get; set; } = "tunnel";

    public string? ProfileId { get; set; }

    public string? Url { get; set; }

    /// <summary>Optional user-renamed tab label.</summary>
    public string? Label { get; set; }
}
