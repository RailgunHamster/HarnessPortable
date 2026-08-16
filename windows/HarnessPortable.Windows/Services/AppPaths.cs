using System.IO;

namespace HarnessPortable.Windows.Services;

public static class AppPaths
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "HarnessPortable");

    public static string ProfilesFile => Path.Combine(DataDirectory, "profiles.json");
    public static string SecretsFile => Path.Combine(DataDirectory, "secrets.json");
    public static string KnownHostsFile => Path.Combine(DataDirectory, "known_hosts.json");

    public static void Ensure()
    {
        Directory.CreateDirectory(DataDirectory);
    }
}
