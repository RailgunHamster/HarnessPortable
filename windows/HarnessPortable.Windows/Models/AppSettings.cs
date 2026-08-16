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
}
