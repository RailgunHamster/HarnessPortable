using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class ProfileStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hp-tests", Guid.NewGuid().ToString());
    private readonly ProfileStore _store;

    public ProfileStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new ProfileStore(Path.Combine(_dir, "profiles.json"));
    }

    [Fact]
    public void SaveAndLoadTunnels_RoundTrips()
    {
        var profile = new TunnelProfile
        {
            Id = "p1",
            Name = "测试机",
            SshHost = "winserver",
            SshPort = 22,
            User = "admin",
            RemoteHost = "127.0.0.1",
            RemotePort = 3080,
            LocalPort = 3080,
        };

        _store.SaveTunnels([profile]);

        var loaded = _store.LoadTunnels();
        Assert.Single(loaded);
        Assert.Equal("测试机", loaded[0].Name);
        Assert.Equal("winserver", loaded[0].SshHost);
        Assert.Equal(3080, loaded[0].RemotePort);
    }

    [Fact]
    public void FindTunnel_WorksByProfileId()
    {
        var profile = new TunnelProfile { Id = "abc", Name = "A", SshHost = "h" };
        _store.SaveTunnels([profile]);

        Assert.NotNull(_store.FindTunnel("abc"));
        Assert.Null(_store.FindTunnel("missing"));
    }

    [Fact]
    public void Directs_AreDeduplicatedAndKeptInOrder()
    {
        _store.SaveDirects(["http://a", "http://b"]);
        _store.SaveDirects(["http://c", "http://a"]);

        var loaded = _store.LoadDirects();
        Assert.Equal(["http://c", "http://a"], loaded);
    }

    [Theory]
    [InlineData("192.168.0.104", "http://192.168.0.104:4096")]
    [InlineData("host:3080", "http://host:3080")]
    [InlineData("http://a/b", "http://a/b")]
    [InlineData("https://a", "https://a")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void NormalizeUrl_MapsAndroidBehavior(string input, string? expected)
    {
        Assert.Equal(expected, ProfileStore.NormalizeUrl(input));
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyConfig()
    {
        var store = new ProfileStore(Path.Combine(_dir, "missing.json"));
        Assert.Empty(store.LoadTunnels());
        Assert.Empty(store.LoadDirects());
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
