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
    public void LegacyPreferencesKeepSettingsAndSafeRestoreDefault()
    {
        File.WriteAllText(Path.Combine(_root, "settings.json"), "{\"CloseBehavior\":\"tray\",\"UpdateSourceSelected\":\"home\",\"UpdateHomeUrl\":\"D:\\\\releases\"}");
        var settings = _settings.Load();
        Assert.Equal("tray", settings.CloseBehavior); Assert.False(settings.ReconnectRestoredWorkspace);
        Assert.Equal(UpdateSourceSlot.Home, UpdateSelection.From(settings).Slot);
        Assert.Empty(settings.Connections);
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
    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }
}
