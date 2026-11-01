using System.IO;
using Velopack.Sources;

namespace HarnessPortable.Windows.Services;

public enum UpdateSourceKind
{
    File,
    Web,
    GitHub,
    Cnb,
}

public static class UpdateSourceFactory
{
    public const string DefaultServerUrl = CnbRepoUrl;
    public const string GitHubRepoUrl = "https://github.com/RailgunHamster/HarnessPortable";
    public const string CnbRepoUrl = "https://cnb.cool/railgunhamster/HarnessPortable";

    public static string Normalize(string? url)
    {
        var trimmed = url?.Trim() ?? "";
        return trimmed.Length == 0 ? CnbRepoUrl : trimmed.TrimEnd('/');
    }

    public static UpdateSourceKind Classify(string? url)
    {
        var value = Normalize(url);
        if (LooksLikeCnb(value)) return UpdateSourceKind.Cnb;
        if (LooksLikeGitHub(value))
        {
            return UpdateSourceKind.GitHub;
        }

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return UpdateSourceKind.Web;
        }

        return UpdateSourceKind.File;
    }

    public static IUpdateSource Create(string? url, UpdateProxyOptions? proxy = null)
    {
        var value = Normalize(url);
        var downloader = new UpdateFileDownloader(proxy ?? new());
        return Classify(value) switch
        {
            UpdateSourceKind.GitHub => new GithubSource(GitHubRepoUrlFrom(value), accessToken: null, prerelease: false, downloader: downloader),
            UpdateSourceKind.Cnb => new CnbUpdateSource(value, downloader),
            UpdateSourceKind.Web => new SimpleWebSource(value, downloader),
            _ => new SimpleFileSource(new DirectoryInfo(value)),
        };
    }

    public static bool IsValid(string? value, bool githubOnly = false, bool cnbOnly = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if(cnbOnly && (!LooksLikeCnb(value) || !ValidCnbPath(new Uri(value))))return false;
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return !string.IsNullOrWhiteSpace(uri.Host) &&
                string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
                (!githubOnly || LooksLikeGitHub(value)) && (!cnbOnly || LooksLikeCnb(value)) &&
                (!(githubOnly || cnbOnly) || uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= 2);
        return !githubOnly && !cnbOnly && Path.IsPathFullyQualified(value.Trim());
    }

    public static bool LooksLikeCnb(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme == "https" && uri.IsDefaultPort && uri.Host.Equals("cnb.cool",StringComparison.OrdinalIgnoreCase);

    private static bool ValidCnbPath(Uri uri)
    {
        var path=uri.AbsolutePath.Split("/-/",StringSplitOptions.None)[0].Trim('/');
        if(path.EndsWith(".git",StringComparison.OrdinalIgnoreCase))path=path[..^4];
        var parts=path.Split('/');
        return parts.Length>=2 && parts.All(p=>p is not ("." or "..") && System.Text.RegularExpressions.Regex.IsMatch(p,"^[A-Za-z0-9_.-]+$"));
    }

    public static bool LooksLikeGitHub(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url.Contains("github.com/", StringComparison.OrdinalIgnoreCase);
        }

        return uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase);
    }

    private static string GitHubRepoUrlFrom(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return GitHubRepoUrl;
        }

        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return GitHubRepoUrl;
        }

        return $"https://github.com/{parts[0]}/{parts[1]}";
    }
}
