using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class LayoutPresetWebStateTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(),
        "hp-preset-" + Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void RoundTripsTheCapturedWebState()
    {
        var store = new LayoutPresetStore(_path);
        store.Save(
        [
            new WorkspaceLayout
            {
                Name = "work",
                Root = new LayoutNode
                {
                    Kind = "pane",
                    Tabs =
                    [
                        new LayoutTabRef
                        {
                            Kind = "tunnel",
                            ProfileId = "p1",
                            Web = new LayoutWebState { SessionId = "session-abc", Sidebar = 0, Rightbar = 320 },
                        },
                    ],
                },
            },
        ]);

        var loaded = Assert.Single(store.Load());
        var tab = Assert.Single(loaded.Root.Tabs);

        Assert.NotNull(tab.Web);
        Assert.Equal("session-abc", tab.Web!.SessionId);
        Assert.Equal(0, tab.Web.Sidebar);
        Assert.Equal(320, tab.Web.Rightbar);
    }

    [Fact]
    public void LoadsPresetsWrittenBeforeTheFieldExisted()
    {
        // Verbatim shape of a 2.6.x preset: no Web member anywhere.
        File.WriteAllText(
            _path,
            """[{"Name":"test","SavedAt":"2026-09-01T18:00:00Z","Root":{"Kind":"pane","Orientation":null,"Children":[],"Tabs":[{"Kind":"direct","ProfileId":null,"Url":"http://127.0.0.1:3080","Label":null}],"DockWidth":null,"DockHeight":null}}]""");

        var loaded = Assert.Single(new LayoutPresetStore(_path).Load());
        var tab = Assert.Single(loaded.Root.Tabs);

        Assert.Equal("http://127.0.0.1:3080", tab.Url);
        Assert.Null(tab.Web);
    }

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
        }
        catch
        {
            // temp file
        }
    }
}
