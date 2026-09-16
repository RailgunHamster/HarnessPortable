using HarnessPortable.Windows.Models;

namespace HarnessPortable.Windows.Services;

/// <summary>Which of the two known update feeds is in use.</summary>
public enum UpdateSourceSlot
{
    Home,
    GitHub,
}

/// <summary>
/// The two update feeds the app can pull from, held as a pair so switching
/// between them never loses the other address.
///
/// Unlike a phone, this machine can read the LAN share directly, so
/// <see cref="Home"/> — not GitHub — is the default.
/// </summary>
public sealed class UpdateSelection
{
    public const string Home = UpdateSourceFactory.DefaultServerUrl;
    public const string GitHub = UpdateSourceFactory.GitHubRepoUrl;

    public UpdateSourceSlot Slot { get; init; } = UpdateSourceSlot.Home;
    public string HomeUrl { get; init; } = Home;
    public string GitHubUrl { get; init; } = GitHub;

    public string Url(UpdateSourceSlot slot) =>
        (slot == UpdateSourceSlot.GitHub ? GitHubUrl : HomeUrl).Trim();

    public string Url() => Url(Slot);

    public string Label(UpdateSourceSlot slot) =>
        slot == UpdateSourceSlot.GitHub ? "GitHub" : "家庭目录";

    /// <summary>
    /// Reads the pair out of settings, folding the pre-two-slot single URL
    /// into the slot it names so an existing install keeps checking where the
    /// user had aimed it. Fills both slots with the built-in defaults.
    /// </summary>
    public static UpdateSelection From(AppSettings settings)
    {
        var home = settings.UpdateHomeUrl?.Trim() ?? "";
        var github = settings.UpdateGitHubUrl?.Trim() ?? "";
        UpdateSourceSlot? slot = ParseSlot(settings.UpdateSourceSelected);
        var legacy = settings.UpdateServerUrl?.Trim() ?? "";

        if (home.Length == 0 && github.Length == 0 && legacy.Length > 0)
        {
            // Nothing split out yet: this single URL is the source. Seed the
            // slot it names, and let the selection follow it unless the
            // settings already say otherwise.
            if (UpdateSourceFactory.Classify(legacy) == UpdateSourceKind.GitHub)
            {
                github = legacy;
                slot ??= UpdateSourceSlot.GitHub;
            }
            else
            {
                home = legacy;
                slot ??= UpdateSourceSlot.Home;
            }
        }

        if (home.Length == 0)
        {
            home = Home;
        }

        if (github.Length == 0)
        {
            github = GitHub;
        }

        return new UpdateSelection
        {
            Slot = slot ?? UpdateSourceSlot.Home,
            HomeUrl = home,
            GitHubUrl = github,
        };
    }

    /// <summary>Writes the pair back, keeping the fresh fields authoritative.</summary>
    public void Store(AppSettings settings)
    {
        settings.UpdateHomeUrl = HomeUrl;
        settings.UpdateGitHubUrl = GitHubUrl;
        settings.UpdateSourceSelected = Slot == UpdateSourceSlot.GitHub ? "github" : "home";
        // Retired field: mirrored only so an older build reading the same file
        // still finds the address it understands.
        settings.UpdateServerUrl = Url();
    }

    public static UpdateSourceSlot? ParseSlot(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "github" => UpdateSourceSlot.GitHub,
        "home" => UpdateSourceSlot.Home,
        _ => null,
    };
}
