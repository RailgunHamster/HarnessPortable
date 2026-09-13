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
    /// Velopack update feed: UNC path, http(s) directory, or GitHub repo URL.
    /// Empty uses the built-in LAN share default.
    /// </summary>
    public string UpdateServerUrl { get; set; } = "";
}
