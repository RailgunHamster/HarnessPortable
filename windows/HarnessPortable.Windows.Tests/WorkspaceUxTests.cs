using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class WorkspaceUxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hp-ux-tests", Guid.NewGuid().ToString());
    private readonly ProfileStore _profiles;
    private readonly AppSettingsStore _settings;
    public WorkspaceUxTests()
    {
        Directory.CreateDirectory(_root);
        _profiles = new(Path.Combine(_root, "profiles.json"));
        _settings = new(Path.Combine(_root, "settings.json"));
        _profiles.Save(new() { Tunnels = [new() { Id = "one", Name = "家庭服务器", SshHost = "home.test", User = "test" }], Directs = ["https://console.test/"] });
    }
    [Fact]
    public void FavoriteAndRecentKeepSeparateConnectionIdentities()
    {
        var library = new ConnectionLibrary(_profiles, _settings);
        library.Update(ConnectionLibrary.TunnelKey("one"), p => { p.Favorite = true; p.Group = "家庭"; });
        library.MarkOpened(ConnectionLibrary.DirectKey("https://console.test/"));
        var entries = new ConnectionLibrary(_profiles, _settings).Load();
        var tunnel = Assert.Single(entries, e => e.Tunnel is not null);
        Assert.True(tunnel.Favorite); Assert.Equal("家庭", tunnel.Group); Assert.Null(tunnel.LastOpened);
        var direct = Assert.Single(entries, e => e.Url is not null);
        Assert.False(direct.Favorite); Assert.NotNull(direct.LastOpened);
    }
    [Theory]
    [InlineData("HOME.TEST", 1)]
    [InlineData("家庭", 1)]
    [InlineData("console.test", 1)]
    [InlineData("missing", 0)]
    public void SearchFindsNamesHostsAndGroups(string query, int expected)
    {
        var library = new ConnectionLibrary(_profiles, _settings);
        library.Update(ConnectionLibrary.TunnelKey("one"), p => p.Group = "家庭");
        Assert.Equal(expected, library.Load(query).Count);
    }
    [Fact]
    public void AutomaticSnapshotDoesNotOverwriteNamedPreset()
    {
        var presets = new LayoutPresetStore(Path.Combine(_root, "layouts.json"));
        presets.Upsert(new() { Name = "日常运维", Root = new() { Tabs = [new() { ProfileId = "one", Label = "预设" }] } });
        var snapshots = new WorkspaceSnapshotStore(Path.Combine(_root, "workspace.json"));
        snapshots.Save(new() { Name = "上次工作区", Root = new() { Tabs = [new() { ProfileId = "one", Label = "当前", Selected = true, Active = true, Web = new() { SessionId = "work", Sidebar = 0 } }] } });
        var restored = Assert.Single(snapshots.Load()!.Root.Tabs);
        Assert.True(restored.Selected); Assert.True(restored.Active); Assert.Equal("work", restored.Web!.SessionId);
        Assert.Equal("预设", Assert.Single(Assert.Single(presets.Load()).Root.Tabs).Label);
    }
    [Fact]
    public void LegacyPreferencesKeepSettingsAndReconnectOnStartup()
    {
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{\"CloseBehavior\":\"tray\",\"UpdateSourceSelected\":\"home\",\"UpdateHomeUrl\":\"D:\\\\releases\"}");
        var settings = _settings.Load();
        Assert.Equal("tray", settings.CloseBehavior); Assert.True(settings.ReconnectRestoredWorkspace);
        Assert.Equal(UpdateSourceSlot.Home, UpdateSelection.From(settings).Slot);
        Assert.Empty(settings.Connections);
    }
    [Fact]
    public void Version271SavedDefaultIsMigratedToReconnect()
    {
        File.WriteAllText(Path.Combine(_root, "settings.json"), """{"ReconnectRestoredWorkspace":false,"RestoreLastLayoutOnStartup":true,"LastLayoutName":"工作区"}""");
        var settings = _settings.Load();
        Assert.True(settings.ReconnectRestoredWorkspace);
        _settings.Save(settings);
        Assert.True(_settings.Load().ReconnectRestoredWorkspace);
        Assert.Equal("工作区", settings.LastLayoutName);
    }
    [Fact]
    public void ExplicitPauseOnStartupIsPreserved()
    {
        _settings.Save(new() { ReconnectRestoredWorkspace = false, StartupReconnectPreference = false });
        Assert.False(new AppSettingsStore(Path.Combine(_root, "settings.json")).Load().ReconnectRestoredWorkspace);
    }
    [Fact]
    public void FiltersDoNotRepeatConnectionsAndRecentUsesOpeningOrder()
    {
        var library = new ConnectionLibrary(_profiles, _settings);
        library.Update(ConnectionLibrary.TunnelKey("one"), p => { p.Favorite = true; p.LastOpened = DateTimeOffset.UtcNow.AddDays(-1); });
        library.MarkOpened(ConnectionLibrary.DirectKey("https://console.test/"));
        Assert.Equal(2, library.Load().Select(e => e.Key).Distinct().Count());
        Assert.Single(library.Load(filter: ConnectionLibraryFilter.Favorites));
        Assert.Equal("direct:https://console.test/", library.Load(filter: ConnectionLibraryFilter.Recent)[0].Key);
        Assert.Single(library.Load("HOME.TEST", ConnectionLibraryFilter.Recent));
    }
    [Theory]
    [InlineData("https://github.com/owner/project", true, true)]
    [InlineData("https://github.com/owner", true, false)]
    [InlineData("https://example.test/feed", false, true)]
    [InlineData("D:\\releases", false, true)]
    [InlineData("not-an-address", false, false)]
    [InlineData("", false, false)]
    public void UpdateAddressValidationAcceptsSupportedFeeds(string address, bool github, bool expected)
        => Assert.Equal(expected, UpdateSourceFactory.IsValid(address, github));

    [Fact]
    public void DragOrderPersistsAcrossTypesAndSearchWithoutLosingHiddenConnections()
    {
        _profiles.Save(new() { Tunnels = [new() { Id = "one", Name = "A", SshHost = "a.test", User = "test" }, new() { Id = "two", Name = "B", SshHost = "b.test", User = "test" }], Directs = ["https://c.test/", "https://d.test/"] });
        var library = new ConnectionLibrary(_profiles, _settings);
        library.Update("tunnel:one", p => { p.Favorite = true; p.Group = "工作"; });
        library.Update("direct:https://d.test/", p => { p.Favorite = true; p.Group = "家庭"; });
        library.MarkOpened("tunnel:one");
        library.MarkOpened("direct:https://d.test/");
        Assert.True(library.Move("direct:https://d.test/", "tunnel:one", false, true));
        var restored = new ConnectionLibrary(_profiles, _settings);
        Assert.Equal(["direct:https://d.test/", "tunnel:one", "tunnel:two", "direct:https://c.test/"], restored.Load().Select(e => e.Key));
        Assert.Equal("工作", restored.Load()[0].Group);
        Assert.Equal("direct:https://d.test/", restored.Load(filter: ConnectionLibraryFilter.Favorites)[0].Key);
        Assert.Single(restored.Load("d.test"));
        Assert.True(restored.Move("tunnel:one", "direct:https://d.test/", false, false));
        Assert.Equal("direct:https://d.test/", restored.Load(filter: ConnectionLibraryFilter.Recent)[0].Key);
        Assert.Equal(4, restored.Load().Count);
        Assert.False(restored.Move("external", "tunnel:one", false, true));
        Assert.False(restored.Move("tunnel:one", "tunnel:one", false, true));
    }

    [Fact]
    public void MovingInFavoritesDoesNotChangeGroup()
    {
        var library = new ConnectionLibrary(_profiles, _settings);
        library.Update("tunnel:one", p => p.Group = "工作");
        library.Update("direct:https://console.test/", p => p.Group = "家庭");
        Assert.True(library.Move("tunnel:one", "direct:https://console.test/", true, false));
        Assert.Equal("工作", library.Load().Single(e => e.Key == "tunnel:one").Group);
    }
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
