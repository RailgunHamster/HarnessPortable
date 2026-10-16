using System.IO;
using Velopack.Sources;

namespace HarnessPortable.Windows.Services;

public enum UpdateSourceKind
{
    File,
    Web,
    GitHub,
    Gitee,
}

public static class UpdateSourceFactory
{
    public const string DefaultServerUrl = @"\\server-home\public\Software\HarnessPortable-Releases";
    public const string GitHubRepoUrl = "https://github.com/RailgunHamster/HarnessPortable";
    public const string GiteeRepoUrl = "https://gitee.com/railgunhamster/HarnessPortable";

    public static string Normalize(string? url)
    {
        var trimmed = url?.Trim() ?? "";
        return trimmed.Length == 0 ? GiteeRepoUrl : trimmed.TrimEnd('/');
    }

    public static UpdateSourceKind Classify(string? url)
    {
        var value = Normalize(url);
        if (LooksLikeGitee(value)) return UpdateSourceKind.Gitee;
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
            UpdateSourceKind.Gitee => new GiteeUpdateSource(value, downloader),
            UpdateSourceKind.Web => new SimpleWebSource(value, downloader),
            _ => new SimpleFileSource(new DirectoryInfo(value)),
        };
    }

    public static bool IsValid(string? value, bool githubOnly = false, bool giteeOnly = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return !string.IsNullOrWhiteSpace(uri.Host) &&
                (!githubOnly || LooksLikeGitHub(value)) && (!giteeOnly || LooksLikeGitee(value)) &&
                (!(githubOnly || giteeOnly) || uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).Length >= 2);
        return !githubOnly && !giteeOnly && Path.IsPathFullyQualified(value.Trim());
    }

    public static bool LooksLikeGitee(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" &&
        (uri.Host.Equals("gitee.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("www.gitee.com", StringComparison.OrdinalIgnoreCase));

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
