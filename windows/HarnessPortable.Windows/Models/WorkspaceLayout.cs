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

    /// <summary>
    /// Per-tab dsh web view state (selected session, sidebar fold state and
    /// width, right panel width) as mirrored into the page URL by the
    /// <c>dsh-view-state</c> dsh plugin. Null when the plugin is not installed,
    /// or when the page carried nothing to record.
    /// </summary>
    public LayoutWebState? Web { get; set; }
}

/// <summary>
/// One tab's captured dsh web view state. Only the plugin's three whitelisted
/// parameters are ever read into it — never the page URL itself, which carries
/// dsh's process token on first navigation.
/// </summary>
public sealed class LayoutWebState
{
    /// <summary>Selected session id, absent when there is none.</summary>
    public string? SessionId { get; set; }

    /// <summary>Sidebar width in px; 0 means collapsed.</summary>
    public int? Sidebar { get; set; }

    /// <summary>
    /// Right panel's saved width in px; 0 means no width was recorded yet.
    /// Whether the panel is currently shown is not part of this state.
    /// </summary>
    public int? Rightbar { get; set; }
}
