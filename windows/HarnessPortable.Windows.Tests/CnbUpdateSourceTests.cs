using System.Text;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;
using Velopack.Logging;
using Velopack.Sources;

namespace HarnessPortable.Windows.Tests;

public sealed class CnbUpdateSourceTests
{
    [Fact]
    public async Task StaticIndexAndVersionedPackageDoNotCallAnApiOrUseCredentials()
    {
        var downloader = new RecordingDownloader();
        var source = new CnbUpdateSource(UpdateSourceFactory.CnbRepoUrl + ".git", downloader);
        var feed = await source.GetReleaseFeed(NullVelopackLogger.Instance, "HarnessPortable", "win", null, null);
        var full = Assert.Single(feed.Assets);
        using var cancel = new CancellationTokenSource();
        var progress = 0;
        await source.DownloadReleaseEntry(NullVelopackLogger.Instance, full, "test.nupkg", p => progress = p, cancel.Token);

        Assert.Equal("/railgunhamster/HarnessPortable/-/git/raw/updates/releases.win.json", new Uri(downloader.Urls[0]).AbsolutePath);
        Assert.Equal("https://cnb.cool/railgunhamster/HarnessPortable/-/releases/download/v2.7.9/HarnessPortable-2.7.9-full.nupkg", downloader.Urls[1]);
        Assert.Equal(cancel.Token, downloader.CancelToken);
        Assert.Equal(100, progress);
        Assert.All(downloader.Headers, headers => Assert.True(headers is null || !headers.ContainsKey("Authorization")));
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
