namespace HarnessPortable.Windows.Services;

public sealed class AppServices
{
    public ProfileStore Profiles { get; } = new();
    public SecureStore Secrets { get; } = new();
    public KnownHostsStore KnownHosts { get; } = new();
    public TunnelManager Tunnels { get; }

    public AppServices()
    {
        AppPaths.Ensure();
        Tunnels = new TunnelManager(Profiles, Secrets, KnownHosts);
    }
}
