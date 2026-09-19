using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class TunnelEngineSmokeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hp-tests", Guid.NewGuid().ToString());
    private readonly ProfileStore _profiles;
    private readonly SecureStore _secrets;
    private readonly KnownHostsStore _knownHosts;
    private readonly TunnelEngine _engine;

    public TunnelEngineSmokeTests()
    {
        Directory.CreateDirectory(_dir);
        _profiles = new ProfileStore(Path.Combine(_dir, "profiles.json"));
        _secrets = new SecureStore(Path.Combine(_dir, "secrets.json"));
        _knownHosts = new KnownHostsStore(Path.Combine(_dir, "known_hosts.json"));
        _engine = new TunnelEngine(_profiles, _secrets, _knownHosts);
    }

    [Fact]
    public void Start_UnreachableHost_FailsTerminally_WithoutEnteringTheRetryLoop()
    {
        var profile = new TunnelProfile
        {
            Id = "p1",
            Name = "不可达",
            SshHost = "127.0.0.1",
            SshPort = 1,
            User = "nobody",
            RemoteHost = "127.0.0.1",
            RemotePort = 3080,
            LocalPort = 3080,
        };
        _profiles.SaveTunnels([profile]);
        _secrets.SetPassword(profile.Id, "wrong-password");

        var seen = new List<TunnelStatus>();
        _engine.State.StateChanged += info =>
        {
            lock (seen)
            {
                seen.Add(info.Status);
            }
        };

        _engine.Start(profile.Id);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var status = _engine.State.Current.Status;
        while (DateTime.UtcNow < deadline &&
               status is TunnelStatus.Idle or TunnelStatus.Connecting)
        {
            Thread.Sleep(50);
            status = _engine.State.Current.Status;
        }

        Assert.Equal(TunnelStatus.Failed, status);

        // Nothing was ever established, so there is nothing to reconnect to:
        // the old watchdog restarted the worker every 45s, resetting the
        // backoff and retrying an unreachable host forever — which is what got
        // the client blocked by the server.
        Thread.Sleep(1_000);
        Assert.Equal(TunnelStatus.Failed, _engine.State.Current.Status);
        lock (seen)
        {
            Assert.DoesNotContain(TunnelStatus.Retrying, seen);
        }

        _engine.Stop();
        Assert.Equal(TunnelStatus.Stopped, _engine.State.Current.Status);
    }

    [Fact]
    public void Start_MissingPasswordAndMissingKey_FailsWithoutRetry()
    {
        var profile = new TunnelProfile
        {
            Id = "p1",
            Name = "无凭据",
            SshHost = "127.0.0.1",
            SshPort = 1,
            User = "nobody",
            RemoteHost = "127.0.0.1",
            RemotePort = 3080,
            LocalPort = 3080,
            IdentityFile = Path.Combine(_dir, "no-such-key"),
        };
        _profiles.SaveTunnels([profile]);

        _engine.Start(profile.Id);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        var status = _engine.State.Current.Status;
        while (DateTime.UtcNow < deadline &&
               status is TunnelStatus.Idle or TunnelStatus.Connecting)
        {
            Thread.Sleep(50);
            status = _engine.State.Current.Status;
        }

        Assert.Equal(TunnelStatus.Failed, status);
        Assert.Contains("私钥", _engine.State.Current.Message);

        Thread.Sleep(400);
        Assert.Equal(TunnelStatus.Failed, _engine.State.Current.Status);
    }

    public void Dispose()
    {
        _engine.Stop(announce: false);
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch
        {
            // Ignore cleanup failures.
        }
    }
}
