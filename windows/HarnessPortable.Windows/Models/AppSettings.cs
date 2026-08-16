namespace HarnessPortable.Windows.Models;

public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    /// <summary>"exit" or "tray".</summary>
    public string CloseBehavior { get; set; } = "exit";
}
