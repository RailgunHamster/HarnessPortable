namespace HarnessPortable.Windows.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>"exit" or "tray".</summary>
    public string CloseBehavior { get; set; } = "exit";

    /// <summary>Restore the last-used workspace layout on startup.</summary>
    public bool RestoreLastLayoutOnStartup { get; set; } = true;

    /// <summary>Name of the last layout that was applied.</summary>
    public string LastLayoutName { get; set; } = "";

    /// <summary>Optional SSH config path override; empty means the current user's ~/.ssh/config.</summary>
    public string SshConfigPath { get; set; } = "";

    /// <summary>
    /// Velopack update feed selected by <see cref="UpdateSourceSelected"/>.
    /// Kept for settings files written before the two-slot model; on load it
    /// is folded into <see cref="UpdateHomeUrl"/> or <see cref="UpdateGitHubUrl"/>.
    /// </summary>
    public string UpdateServerUrl { get; set; } = "";

    /// <summary>LAN release share the PC reads directly.</summary>
    public string UpdateHomeUrl { get; set; } = "";

    /// <summary>GitHub repository the feed is pulled from.</summary>
    public string UpdateGitHubUrl { get; set; } = "";

    /// <summary>"home" or "github" — which of the two a check uses.</summary>
    public string UpdateSourceSelected { get; set; } = "";
}
