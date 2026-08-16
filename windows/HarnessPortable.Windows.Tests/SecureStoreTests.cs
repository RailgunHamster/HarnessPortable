using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class SecureStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hp-tests", Guid.NewGuid().ToString());
    private readonly SecureStore _store;

    public SecureStoreTests()
    {
        Directory.CreateDirectory(_dir);
        _store = new SecureStore(Path.Combine(_dir, "secrets.json"));
    }

    [Fact]
    public void Password_RoundTrips()
    {
        const string profileId = "p1";
        const string password = "s3cret-密码";

        Assert.False(_store.HasPassword(profileId));
        _store.SetPassword(profileId, password);

        Assert.True(_store.HasPassword(profileId));
        Assert.Equal(password, _store.GetPassword(profileId));
    }

    [Fact]
    public void ClearPassword_RemovesEntry()
    {
        _store.SetPassword("p1", "secret");
        _store.ClearPassword("p1");

        Assert.False(_store.HasPassword("p1"));
        Assert.Null(_store.GetPassword("p1"));
    }

    [Fact]
    public void MissingPassword_ReturnsNull()
    {
        Assert.Null(_store.GetPassword("missing"));
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
