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
        Assert.Equal("exit", _store.Load().CloseBehavior);
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        _store.Save(new AppSettings { CloseBehavior = "tray" });

        Assert.Equal("tray", _store.Load().CloseBehavior);
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
