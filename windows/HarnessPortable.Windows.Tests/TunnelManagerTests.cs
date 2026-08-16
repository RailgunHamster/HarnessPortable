using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class TunnelManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hp-tests", Guid.NewGuid().ToString());
    private readonly ProfileStore _profiles;
    private readonly SecureStore _secrets;
    private readonly KnownHostsStore _knownHosts;
    private readonly TunnelManager _manager;

    public TunnelManagerTests()
    {
        Directory.CreateDirectory(_dir);
        _profiles = new ProfileStore(Path.Combine(_dir, "profiles.json"));
        _secrets = new SecureStore(Path.Combine(_dir, "secrets.json"));
        _knownHosts = new KnownHostsStore(Path.Combine(_dir, "known_hosts.json"));
        _manager = new TunnelManager(_profiles, _secrets, _knownHosts);
    }

    [Fact]
    public void TwoProfiles_CanRunAtTheSameTime_AndStopIndependently()
    {
        var first = AddProfile("p1", "隧道一", 1);
        var second = AddProfile("p2", "隧道二", 2);

        _manager.Start(first.Id);
        _manager.Start(second.Id);

        WaitUntil(() => _manager.GetActiveStates().Count() == 2);
        Assert.Equal(2, _manager.GetActiveStates().Count());

        _manager.Stop(first.Id);
        WaitUntil(() => _manager.GetActiveStates().Count() == 1);

        var remaining = _manager.GetActiveStates().Single();
        Assert.Equal(second.Id, remaining.ProfileId);
    }

    [Fact]
    public void StopAll_TerminatesEveryTunnel()
    {
        AddProfile("p1", "隧道一", 1);
        AddProfile("p2", "隧道二", 2);

        _manager.Start("p1");
        _manager.Start("p2");
        WaitUntil(() => _manager.GetActiveStates().Count() == 2);

        _manager.StopAll();

        WaitUntil(() => !_manager.GetActiveStates().Any());
        Assert.Empty(_manager.GetActiveStates());
    }

    private TunnelProfile AddProfile(string id, string name, int sshPort)
    {
        var profile = new TunnelProfile
        {
            Id = id,
            Name = name,
            SshHost = "127.0.0.1",
            SshPort = sshPort,
            User = "nobody",
            RemoteHost = "127.0.0.1",
            RemotePort = 3080,
            LocalPort = 3080 + sshPort,
        };
        var profiles = _profiles.LoadTunnels();
        profiles.Add(profile);
        _profiles.SaveTunnels(profiles);
        _secrets.SetPassword(id, "wrong-password");
        return profile;
    }

    private static void WaitUntil(Func<bool> condition, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            Thread.Sleep(50);
        }

        Assert.Fail("Timed out waiting for tunnel manager state");
    }

    public void Dispose()
    {
        _manager.StopAll();
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
