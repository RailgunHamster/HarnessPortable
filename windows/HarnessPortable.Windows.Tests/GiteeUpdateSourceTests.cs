using System.Text;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;
using Velopack.Logging;
using Velopack.Sources;

namespace HarnessPortable.Windows.Tests;

public sealed class GiteeUpdateSourceTests
{
    [Fact]
    public async Task StaticIndexAndVersionedPackageDoNotCallAnApiOrUseCredentials()
    {
        var downloader = new RecordingDownloader();
        var source = new GiteeUpdateSource(UpdateSourceFactory.GiteeRepoUrl + ".git", downloader);
        var feed = await source.GetReleaseFeed(NullVelopackLogger.Instance, "HarnessPortable", "win", null, null);
        var full = Assert.Single(feed.Assets);
        using var cancel = new CancellationTokenSource();
        var progress = 0;
        await source.DownloadReleaseEntry(NullVelopackLogger.Instance, full, "test.nupkg", p => progress = p, cancel.Token);

        Assert.Equal("/railgunhamster/HarnessPortable/raw/updates/releases.win.json", new Uri(downloader.Urls[0]).AbsolutePath);
        Assert.Equal("https://gitee.com/railgunhamster/HarnessPortable/releases/download/v2.7.9/HarnessPortable-2.7.9-full.nupkg", downloader.Urls[1]);
        Assert.Equal(cancel.Token, downloader.CancelToken);
        Assert.Equal(100, progress);
        Assert.All(downloader.Headers, headers => Assert.True(headers is null || !headers.ContainsKey("Authorization")));
    }

    [Theory]
    [InlineData("https://github.com/RailgunHamster/HarnessPortable")]
    [InlineData("https://github.com/RailgunHamster/HarnessPortable/releases/latest/download")]
    public void OfficialGitHubMigratesButLaterManualSelectionIsRespected(string url)
    {
        var settings = new AppSettings { UpdateSourceSelected = "github", UpdateGitHubUrl = url, UpdateHomeUrl = @"\\custom\releases" };
        var migrated = UpdateSelection.From(settings);
        Assert.Equal(UpdateSourceSlot.Gitee, migrated.Slot);
        Assert.Equal(@"\\custom\releases", migrated.HomeUrl);
        migrated.Store(settings);
        settings.UpdateSourceSelected = "github";
        Assert.Equal(UpdateSourceSlot.GitHub, UpdateSelection.From(settings).Slot);
    }

    [Fact]
    public void HomeAndCustomGitHubSelectionsStaySelected()
    {
        Assert.Equal(UpdateSourceSlot.Home, UpdateSelection.From(new AppSettings { UpdateSourceSelected = "home" }).Slot);
        Assert.Equal(UpdateSourceSlot.GitHub, UpdateSelection.From(new AppSettings
        { UpdateSourceSelected = "github", UpdateGitHubUrl = "https://github.com/example/custom" }).Slot);
    }

    [Fact]
    public void SwitchingAmongThreeSourcesKeepsEachTypedAddressInItsOwnSlot()
    {
        var selection = new UpdateSelection();
        selection = UpdateSelection.MergeTyped(selection, "gitee", "https://gitee.com/example/custom", "home");
        selection = UpdateSelection.MergeTyped(selection, "home", @"\\nas\releases", "github");
        selection = UpdateSelection.MergeTyped(selection, "github", "https://github.com/example/custom", "gitee");
        Assert.Equal("https://gitee.com/example/custom", selection.Url());
        Assert.Equal(@"\\nas\releases", selection.HomeUrl);
        Assert.Equal("https://github.com/example/custom", selection.GitHubUrl);
    }

    private sealed class RecordingDownloader : IFileDownloader
    {
        public List<string> Urls { get; } = [];
        public List<IDictionary<string, string>?> Headers { get; } = [];
        public CancellationToken CancelToken { get; private set; }
        public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
        {
            Urls.Add(url); Headers.Add(headers);
            return Task.FromResult("""{"Assets":[{"PackageId":"HarnessPortable","Version":"2.7.9","Type":"Full","FileName":"HarnessPortable-2.7.9-full.nupkg","Size":123,"SHA1":"ABC","SHA256":"DEF"}]}""");
        }
        public async Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30) =>
            Encoding.UTF8.GetBytes(await DownloadString(url, headers, timeout));
        public Task DownloadFile(string url, string targetFile, Action<int> progress,
            IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default)
        {
            Urls.Add(url); Headers.Add(headers); CancelToken = cancelToken; progress(100);
            return Task.CompletedTask;
        }
    }
}
