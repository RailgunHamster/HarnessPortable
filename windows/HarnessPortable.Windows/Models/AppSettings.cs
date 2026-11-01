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

    public bool ReconnectRestoredWorkspace { get; set; } = true;
    /// <summary>Explicit choice, distinct from the false default saved by 2.7.1.</summary>
    public bool? StartupReconnectPreference { get; set; }
    public bool ConnectionSidebarCollapsed { get; set; }
    public string Appearance { get; set; } = "system";
    public Dictionary<string, ConnectionPreference> Connections { get; set; } = [];
    public List<string> ConnectionOrder { get; set; } = [];

    /// <summary>Optional SSH config path override; empty means the current user's ~/.ssh/config.</summary>
    public string SshConfigPath { get; set; } = "";

    /// <summary>
    /// Velopack update feed selected by <see cref="UpdateSourceSelected"/>.
    /// Kept for settings files written before the two-slot model; on load it
    /// is folded into the surviving CNB / GitHub selection.
    /// </summary>
    public string UpdateServerUrl { get; set; } = "";

    /// <summary>GitHub repository the feed is pulled from.</summary>
    public string UpdateGitHubUrl { get; set; } = "";

    public string UpdateCnbUrl { get; set; } = "";

    /// <summary>"cnb" or "github". Retired selections migrate to CNB.</summary>
    public string UpdateSourceSelected { get; set; } = "";

    public bool UpdateProxyEnabled { get; set; }
    public string UpdateProxyAddress { get; set; } = "localhost:7890";

    /// <summary>
    /// Web-login mode per direct URL: "nssm" = auto-login from this machine's
    /// NSSM-hosted dsh log, missing/"none" = open as-is. Kept in settings.json
    /// (per-platform), not in the cross-platform profiles.json directs list.
    /// </summary>
    public Dictionary<string, string> DirectAuthModes { get; set; } = [];
}

public sealed class ConnectionPreference
{
    public bool Favorite { get; set; }
    public string Group { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset? LastOpened { get; set; }
}
