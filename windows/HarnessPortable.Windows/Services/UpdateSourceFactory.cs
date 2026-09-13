using System.IO;
using Velopack.Sources;

namespace HarnessPortable.Windows.Services;

public enum UpdateSourceKind
{
    File,
    Web,
    GitHub,
}

public static class UpdateSourceFactory
{
    public const string DefaultServerUrl = @"\\server-home\public\Software\HarnessPortable-Releases";
    public const string GitHubRepoUrl = "https://github.com/RailgunHamster/HarnessPortable";

    public static string Normalize(string? url)
    {
        var trimmed = url?.Trim() ?? "";
        return trimmed.Length == 0 ? DefaultServerUrl : trimmed.TrimEnd('/');
    }

    public static UpdateSourceKind Classify(string? url)
    {
        var value = Normalize(url);
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

    public static IUpdateSource Create(string? url)
    {
        var value = Normalize(url);
        return Classify(value) switch
        {
            UpdateSourceKind.GitHub => new GithubSource(GitHubRepoUrlFrom(value), accessToken: null, prerelease: false),
            UpdateSourceKind.Web => new SimpleWebSource(value),
            _ => new SimpleFileSource(new DirectoryInfo(value)),
        };
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
