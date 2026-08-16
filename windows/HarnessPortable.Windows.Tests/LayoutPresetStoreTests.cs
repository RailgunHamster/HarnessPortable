using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class LayoutPresetStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hp-tests", Guid.NewGuid().ToString());
    private readonly LayoutPresetStore _store;

    public LayoutPresetStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new LayoutPresetStore(Path.Combine(_dir, "layouts.json"));
    }

    [Fact]
    public void Layout_RoundTrips()
    {
        var layout = new WorkspaceLayout
        {
            Name = "三列",
            Root = new LayoutNode
            {
                Kind = "split",
                Orientation = "horizontal",
                Children =
                [
                    new LayoutNode
                    {
                        Kind = "pane",
                        Tabs = [new LayoutTabRef { Kind = "management" }],
                    },
                    new LayoutNode
                    {
                        Kind = "pane",
                        Tabs = [new LayoutTabRef { Kind = "tunnel", ProfileId = "p1", Label = "生产环境" }],
                    },
                    new LayoutNode
                    {
                        Kind = "split",
                        Orientation = "vertical",
                        Children =
                        [
                            new LayoutNode
                            {
                                Kind = "pane",
                                Tabs =
                                [
                                    new LayoutTabRef { Kind = "tunnel", ProfileId = "p2" },
                                    new LayoutTabRef { Kind = "direct", Url = "http://a:4096" },
                                ],
                            },
                            new LayoutNode
                            {
                                Kind = "pane",
                                Tabs = [new LayoutTabRef { Kind = "direct", Url = "http://b:4096" }],
                            },
                        ],
                    },
                ],
            },
        };

        _store.Upsert(layout);
        var loaded = _store.Load();

        var restored = Assert.Single(loaded);
        Assert.Equal("三列", restored.Name);
        Assert.Equal("split", restored.Root.Kind);
        Assert.Equal(3, restored.Root.Children.Count);
        Assert.Equal("management", restored.Root.Children[0].Tabs[0].Kind);
        Assert.Equal("生产环境", restored.Root.Children[1].Tabs[0].Label);
        Assert.Equal("p2", restored.Root.Children[2].Children[0].Tabs[0].ProfileId);
        Assert.Equal("http://a:4096", restored.Root.Children[2].Children[0].Tabs[1].Url);
    }

    [Fact]
    public void Upsert_ReplacesSameName()
    {
        _store.Upsert(new WorkspaceLayout { Name = "工作区" });
        _store.Upsert(new WorkspaceLayout { Name = "工作区" });

        Assert.Single(_store.Load());
    }

    [Fact]
    public void Delete_RemovesByName()
    {
        _store.Upsert(new WorkspaceLayout { Name = "工作区" });
        _store.Delete("工作区");

        Assert.Empty(_store.Load());
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}
