using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public class LocalNssmAuthTests
{
    [Fact]
    public void GraftToken_KeepsDestinationAndAddsToken()
    {
        var result = LocalNssmAuth.GraftToken(
            "http://localhost:3080", "http://127.0.0.1:3080/?token=abc");

        Assert.NotNull(result);
        Assert.StartsWith("http://localhost:3080", result);
        Assert.Contains("token=abc", result);
        Assert.DoesNotContain("127.0.0.1", result);
    }

    [Fact]
    public void GraftToken_PreservesPathAndOtherParameters()
    {
        var result = LocalNssmAuth.GraftToken(
            "http://localhost:3080/s/abc?dsh_sidebar=1", "http://127.0.0.1:3080/?token=abc");

        Assert.NotNull(result);
        Assert.Contains("/s/abc", result);
        Assert.Contains("dsh_sidebar=1", result);
        Assert.Contains("token=abc", result);
    }

    [Fact]
    public void GraftToken_ReplacesStaleTokenItem()
    {
        var result = LocalNssmAuth.GraftToken(
            "http://localhost:3080/?token=old", "http://127.0.0.1:3080/?token=new");

        Assert.NotNull(result);
        Assert.DoesNotContain("token=old", result);
        Assert.Contains("token=new", result);
    }

    [Fact]
    public void GraftToken_RejectsPortMismatch()
    {
        // The log belongs to a different instance listening elsewhere.
        Assert.Null(LocalNssmAuth.GraftToken(
            "http://localhost:3080", "http://127.0.0.1:4096/?token=abc"));
    }

    [Fact]
    public void GraftToken_RejectsAuthUrlWithoutToken()
    {
        Assert.Null(LocalNssmAuth.GraftToken(
            "http://localhost:3080", "http://127.0.0.1:3080/"));
    }

    [Fact]
    public void GraftToken_RejectsInvalidInput()
    {
        Assert.Null(LocalNssmAuth.GraftToken(null!, "http://127.0.0.1:3080/?token=abc"));
        Assert.Null(LocalNssmAuth.GraftToken("http://localhost:3080", ""));
        Assert.Null(LocalNssmAuth.GraftToken("not a url", "http://127.0.0.1:3080/?token=abc"));
        Assert.Null(LocalNssmAuth.GraftToken("http://localhost:3080", "not a url"));
    }

    [Fact]
    public async Task Settings_PersistDirectAuthModesPerUrl()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hp-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);

        try
        {
            var store = new AppSettingsStore(Path.Combine(directory, "settings.json"));

            var settings = new AppSettings();
            settings.DirectAuthModes["http://localhost:3080"] = LocalNssmAuth.ModeNssm;
            store.Save(settings);

            var loaded = store.Load();
            Assert.Equal(LocalNssmAuth.ModeNssm, loaded.DirectAuthModes["http://localhost:3080"]);
            Assert.False(loaded.DirectAuthModes.TryGetValue("http://other:3080", out _));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
