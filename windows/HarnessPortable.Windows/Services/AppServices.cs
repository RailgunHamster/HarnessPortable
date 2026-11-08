namespace HarnessPortable.Windows.Services;

public sealed class AppServices
{
    public ProfileStore Profiles { get; }
    public SecureStore Secrets { get; }
    public KnownHostsStore KnownHosts { get; }
    public AppSettingsStore Settings { get; }
    public TunnelManager Tunnels { get; }
    public UpdateService Updates { get; }
    public ConfigurationSyncService Sync { get; }

    public AppServices(string? dataDirectory = null)
    {
        var directory = dataDirectory ?? AppPaths.DataDirectory;
        System.IO.Directory.CreateDirectory(directory);
        Profiles = new(System.IO.Path.Combine(directory, "profiles.json"));
        Secrets = new(System.IO.Path.Combine(directory, "secrets.json"));
        KnownHosts = new(System.IO.Path.Combine(directory, "known_hosts.json"));
        Settings = new(System.IO.Path.Combine(directory, "settings.json"));
        Updates = new UpdateService(Settings);
        Tunnels = new TunnelManager(Profiles, Secrets, KnownHosts);
        Sync = new ConfigurationSyncService(this, directory);
    }
}
