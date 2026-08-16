namespace HarnessPortable.Windows.Services;

public sealed class AppServices
{
    public ProfileStore Profiles { get; } = new();
    public SecureStore Secrets { get; } = new();
    public KnownHostsStore KnownHosts { get; } = new();
    public TunnelEngine Tunnel { get; }

    public AppServices()
    {
        AppPaths.Ensure();
        Tunnel = new TunnelEngine(Profiles, Secrets, KnownHosts);
    }
}
