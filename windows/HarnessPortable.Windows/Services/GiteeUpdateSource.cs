using System.IO;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace HarnessPortable.Windows.Services;

/// <summary>Reads a public static index, then downloads versioned release attachments. No API token is needed.</summary>
public sealed class GiteeUpdateSource : IUpdateSource
{
    private readonly string _repository;
    private readonly IFileDownloader _downloader;

    public GiteeUpdateSource(string repository, IFileDownloader downloader)
    {
        var uri = new Uri(repository);
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (!UpdateSourceFactory.LooksLikeGitee(repository) || parts.Length < 2)
            throw new ArgumentException("请输入完整的 Gitee 仓库地址。", nameof(repository));
        var repo = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        _repository = $"https://gitee.com/{parts[0]}/{repo}";
        _downloader = downloader;
    }

    public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string? channel,
        Guid? stagingId, VelopackAsset? latestLocalRelease) =>
        new SimpleWebSource(_repository + "/raw/updates", _downloader)
            .GetReleaseFeed(logger, appId, channel ?? "win", stagingId, latestLocalRelease);

    public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
        Action<int> progress, CancellationToken cancelToken = default)
    {
        if (string.IsNullOrWhiteSpace(releaseEntry.FileName) ||
            releaseEntry.FileName.IndexOfAny(['/', '\\']) >= 0 || releaseEntry.FileName != Path.GetFileName(releaseEntry.FileName))
            throw new ArgumentException("更新索引包含无效的安装包文件名。", nameof(releaseEntry));
        return new SimpleWebSource(_repository + "/releases/download/v" + releaseEntry.Version, _downloader)
            .DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
    }
}
