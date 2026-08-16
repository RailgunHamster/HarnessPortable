using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class KnownHostsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hp-tests", Guid.NewGuid().ToString());
    private readonly KnownHostsStore _store;

    public KnownHostsStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new KnownHostsStore(Path.Combine(_dir, "known_hosts.json"));
    }

    [Fact]
    public void FirstKey_IsTrustedAndRemembered()
    {
        var key = new byte[] { 1, 2, 3, 4 };

        var first = _store.Check("host", 22, "ssh-ed25519", key);
        var second = _store.Check("host", 22, "ssh-ed25519", key);

        Assert.Equal(HostKeyDecision.TrustedNew, first);
        Assert.Equal(HostKeyDecision.TrustedMatch, second);
    }

    [Fact]
    public void ChangedKey_IsRejected()
    {
        var firstKey = new byte[] { 1, 2, 3, 4 };
        var changedKey = new byte[] { 9, 9, 9, 9 };

        _store.Check("host", 22, "ssh-ed25519", firstKey);
        var result = _store.Check("host", 22, "ssh-ed25519", changedKey);

        Assert.Equal(HostKeyDecision.Changed, result);
    }

    [Fact]
    public void KeyTypeChange_IsRejected()
    {
        var key = new byte[] { 1, 2, 3, 4 };

        _store.Check("host", 22, "ssh-ed25519", key);
        var result = _store.Check("host", 22, "ssh-rsa", key);

        Assert.Equal(HostKeyDecision.Changed, result);
    }

    [Fact]
    public void DifferentPorts_AreIndependent()
    {
        var key = new byte[] { 1, 2, 3 };

        var first = _store.Check("host", 22, "ssh-ed25519", key);
        var second = _store.Check("host", 2222, "ssh-ed25519", key);

        Assert.Equal(HostKeyDecision.TrustedNew, first);
        Assert.Equal(HostKeyDecision.TrustedNew, second);
    }

    public void Dispose()
    {
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
