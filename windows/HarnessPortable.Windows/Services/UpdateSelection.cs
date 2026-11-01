using HarnessPortable.Windows.Models;

namespace HarnessPortable.Windows.Services;

public enum UpdateSourceSlot { Cnb, GitHub }

/// <summary>CNB is the default; GitHub remains an independently saved alternative.</summary>
public sealed record UpdateSelection
{
    public const string Cnb = UpdateSourceFactory.CnbRepoUrl;
    public const string GitHub = UpdateSourceFactory.GitHubRepoUrl;
    public UpdateSourceSlot Slot { get; init; } = UpdateSourceSlot.Cnb;
    public string CnbUrl { get; init; } = Cnb;
    public string GitHubUrl { get; init; } = GitHub;
    public string Url(UpdateSourceSlot slot) => (slot == UpdateSourceSlot.GitHub ? GitHubUrl : CnbUrl).Trim();
    public string Url() => Url(Slot);
    public string Label(UpdateSourceSlot slot) => slot == UpdateSourceSlot.GitHub ? "GitHub" : "CNB";
    public static string Tag(UpdateSourceSlot slot) => slot.ToString().ToLowerInvariant();
    public UpdateSelection WithUrl(UpdateSourceSlot slot, string url) => slot == UpdateSourceSlot.GitHub
        ? this with { GitHubUrl=url } : this with { CnbUrl=url };
    public static UpdateSelection From(AppSettings settings)
    {
        var github=settings.UpdateGitHubUrl?.Trim() ?? "";
        if (github.Length==0 && UpdateSourceFactory.LooksLikeGitHub(settings.UpdateServerUrl)) github=settings.UpdateServerUrl.Trim();
        var cnb=settings.UpdateCnbUrl?.Trim() ?? "";
        var slot=ParseSlot(settings.UpdateSourceSelected) ?? UpdateSourceSlot.Cnb;
        // A saved CNB address marks the one-time migration; later explicit GitHub choices survive.
        if(cnb.Length==0 && slot==UpdateSourceSlot.GitHub && (github.Length==0 || IsOfficialGitHub(github))) slot=UpdateSourceSlot.Cnb;
        return new(){Slot=slot,CnbUrl=cnb.Length==0 ? Cnb : cnb,GitHubUrl=github.Length==0 ? GitHub : github};
    }
    private static bool IsOfficialGitHub(string url) => UpdateSourceFactory.LooksLikeGitHub(url) && Uri.TryCreate(url,UriKind.Absolute,out var uri) &&
        (uri.AbsolutePath.Trim('/').Equals("RailgunHamster/HarnessPortable",StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.Trim('/').StartsWith("RailgunHamster/HarnessPortable/",StringComparison.OrdinalIgnoreCase));
    public static UpdateSelection MergeTyped(UpdateSelection stored,string? leavingTag,string typed,string? enteringTag) =>
        stored.WithUrl(ParseSlot(leavingTag) ?? stored.Slot,typed) with {Slot=ParseSlot(enteringTag) ?? stored.Slot};
    public void Store(AppSettings settings)
    {
        settings.UpdateCnbUrl=CnbUrl;settings.UpdateGitHubUrl=GitHubUrl;settings.UpdateSourceSelected=Tag(Slot);
        settings.UpdateServerUrl=Url();
    }
    public static UpdateSourceSlot? ParseSlot(string? value)=>value?.Trim().ToLowerInvariant() switch
    {"cnb"=>UpdateSourceSlot.Cnb,"github"=>UpdateSourceSlot.GitHub,_=>null};
}
