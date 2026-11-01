using System.IO;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace HarnessPortable.Windows.Services;

/// <summary>Public CNB static feed and versioned assets; no maintainer token or API calls.</summary>
public sealed class CnbUpdateSource : IUpdateSource
{
    private readonly string _repository;
    private readonly IFileDownloader _downloader;
    public CnbUpdateSource(string repository,IFileDownloader downloader)
    {
        if(!UpdateSourceFactory.IsValid(repository,cnbOnly:true))throw new ArgumentException("请输入完整的 CNB 仓库地址。",nameof(repository));
        var path=new Uri(repository).AbsolutePath.Split("/-/",StringSplitOptions.None)[0].Trim('/');
        if(path.EndsWith(".git",StringComparison.OrdinalIgnoreCase))path=path[..^4];
        _repository="https://cnb.cool/"+path;_downloader=downloader;
    }
    public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger,string? appId,string? channel,Guid? stagingId,VelopackAsset? latestLocalRelease)=>
        new SimpleWebSource(_repository+"/-/git/raw/updates",_downloader).GetReleaseFeed(logger,appId,channel ?? "win",stagingId,latestLocalRelease);
    public Task DownloadReleaseEntry(IVelopackLogger logger,VelopackAsset releaseEntry,string localFile,Action<int> progress,CancellationToken cancelToken=default)
    {
        if(string.IsNullOrWhiteSpace(releaseEntry.FileName)||releaseEntry.FileName.IndexOfAny(['/','\\'])>=0||releaseEntry.FileName!=Path.GetFileName(releaseEntry.FileName))
            throw new ArgumentException("更新索引包含无效的安装包文件名。",nameof(releaseEntry));
        return new SimpleWebSource(_repository+"/-/releases/download/v"+releaseEntry.Version,_downloader)
            .DownloadReleaseEntry(logger,releaseEntry,localFile,progress,cancelToken);
    }
}
