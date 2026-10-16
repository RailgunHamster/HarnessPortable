using HarnessPortable.Windows.Models;

namespace HarnessPortable.Windows.Services;

public enum UpdateSourceSlot { Home, GitHub, Gitee }

/// <summary>Saved independent addresses; official GitHub installs migrate once to Gitee.</summary>
public sealed record UpdateSelection
{
    public const string Home = UpdateSourceFactory.DefaultServerUrl;
    public const string GitHub = UpdateSourceFactory.GitHubRepoUrl;
    public const string Gitee = UpdateSourceFactory.GiteeRepoUrl;

    public UpdateSourceSlot Slot { get; init; } = UpdateSourceSlot.Gitee;
    public string HomeUrl { get; init; } = Home;
    public string GitHubUrl { get; init; } = GitHub;
    public string GiteeUrl { get; init; } = Gitee;

    public string Url(UpdateSourceSlot slot) => (slot switch
    {
        UpdateSourceSlot.GitHub => GitHubUrl,
        UpdateSourceSlot.Gitee => GiteeUrl,
        _ => HomeUrl,
    }).Trim();
    public string Url() => Url(Slot);
    public string Label(UpdateSourceSlot slot) => slot switch
    {
        UpdateSourceSlot.GitHub => "GitHub",
        UpdateSourceSlot.Gitee => "Gitee",
        _ => "家庭目录",
    };
    public static string Tag(UpdateSourceSlot slot) => slot.ToString().ToLowerInvariant();

    public UpdateSelection WithUrl(UpdateSourceSlot slot, string url) => slot switch
    {
        UpdateSourceSlot.GitHub => this with { GitHubUrl = url },
        UpdateSourceSlot.Gitee => this with { GiteeUrl = url },
        _ => this with { HomeUrl = url },
    };

    public static UpdateSelection From(AppSettings settings)
    {
        var home = settings.UpdateHomeUrl?.Trim() ?? "";
        var github = settings.UpdateGitHubUrl?.Trim() ?? "";
        var gitee = settings.UpdateGiteeUrl?.Trim() ?? "";
        var slot = ParseSlot(settings.UpdateSourceSelected);
        var legacy = settings.UpdateServerUrl?.Trim() ?? "";
        if (home.Length == 0 && github.Length == 0 && gitee.Length == 0 && legacy.Length > 0)
        {
            switch (UpdateSourceFactory.Classify(legacy))
            {
                case UpdateSourceKind.GitHub: github = legacy; slot ??= UpdateSourceSlot.GitHub; break;
                case UpdateSourceKind.Gitee: gitee = legacy; slot ??= UpdateSourceSlot.Gitee; break;
                default: home = legacy; slot ??= UpdateSourceSlot.Home; break;
            }
        }
        // A populated Gitee slot marks completion, so choosing GitHub later is respected.
        if (string.IsNullOrWhiteSpace(settings.UpdateGiteeUrl) && slot == UpdateSourceSlot.GitHub &&
            (github.Length == 0 || IsOfficialGitHub(github))) slot = UpdateSourceSlot.Gitee;
        return new UpdateSelection
        {
            Slot = slot ?? UpdateSourceSlot.Gitee,
            HomeUrl = home.Length == 0 ? Home : home,
            GitHubUrl = github.Length == 0 ? GitHub : github,
            GiteeUrl = gitee.Length == 0 ? Gitee : gitee,
        };
    }

    private static bool IsOfficialGitHub(string url) =>
        UpdateSourceFactory.LooksLikeGitHub(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        (uri.AbsolutePath.Trim('/').Equals("RailgunHamster/HarnessPortable", StringComparison.OrdinalIgnoreCase) ||
         uri.AbsolutePath.Trim('/').StartsWith("RailgunHamster/HarnessPortable/", StringComparison.OrdinalIgnoreCase));

    /// <summary>The typed address belongs to the slot being left.</summary>
    public static UpdateSelection MergeTyped(UpdateSelection stored, string? leavingTag, string typed, string? enteringTag) =>
        stored.WithUrl(ParseSlot(leavingTag) ?? stored.Slot, typed) with
        { Slot = ParseSlot(enteringTag) ?? stored.Slot };

    public void Store(AppSettings settings)
    {
        settings.UpdateHomeUrl = HomeUrl;
        settings.UpdateGitHubUrl = GitHubUrl;
        settings.UpdateGiteeUrl = GiteeUrl;
        settings.UpdateSourceSelected = Tag(Slot);
        settings.UpdateServerUrl = Url();
    }

    public static UpdateSourceSlot? ParseSlot(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "github" => UpdateSourceSlot.GitHub,
        "gitee" => UpdateSourceSlot.Gitee,
        "home" => UpdateSourceSlot.Home,
        _ => null,
    };
}
