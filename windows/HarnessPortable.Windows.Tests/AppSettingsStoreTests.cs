using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class AppSettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hp-tests", Guid.NewGuid().ToString());
    private readonly AppSettingsStore _store;

    public AppSettingsStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new AppSettingsStore(Path.Combine(_dir, "settings.json"));
    }

    [Fact]
    public void MissingFile_ReturnsDefaultExitBehavior()
    {
        var settings = _store.Load();

        Assert.Equal("exit", settings.CloseBehavior);
        Assert.True(settings.RestoreLastLayoutOnStartup);
        Assert.Equal("", settings.LastLayoutName);
        Assert.Equal("", settings.UpdateServerUrl);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        _store.Save(new AppSettings
        {
            CloseBehavior = "tray",
            RestoreLastLayoutOnStartup = false,
            LastLayoutName = "三列",
            UpdateServerUrl = @"\\server-home\public\Software\HarnessPortable-Releases",
        });

        var settings = _store.Load();
        Assert.Equal("tray", settings.CloseBehavior);
        Assert.False(settings.RestoreLastLayoutOnStartup);
        Assert.Equal("三列", settings.LastLayoutName);
        Assert.Equal(@"\\server-home\public\Software\HarnessPortable-Releases", settings.UpdateServerUrl);
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
