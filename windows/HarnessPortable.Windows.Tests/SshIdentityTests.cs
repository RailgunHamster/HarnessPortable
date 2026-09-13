using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class SshIdentityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hp-tests", Guid.NewGuid().ToString());

    public SshIdentityTests()
    {
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void ExpandPath_ResolvesTildeAndRelativeNames()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(Path.GetFullPath(home), SshIdentity.ExpandPath("~"));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(home, ".ssh", "id_ed25519")),
            SshIdentity.ExpandPath("~/.ssh/id_ed25519"));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(_dir, "id_ed25519")),
            SshIdentity.ExpandPath("id_ed25519", _dir));
    }

    [Fact]
    public void CandidateFiles_SpecifiedIdentityIsExclusive()
    {
        var missing = Path.Combine(_dir, "missing-key");
        File.WriteAllText(Path.Combine(_dir, "id_ed25519"), "not-a-key");

        var specified = SshIdentity.CandidateFiles(missing, _dir);
        Assert.Equal([Path.GetFullPath(missing)], specified);

        var defaults = SshIdentity.CandidateFiles(null, _dir);
        Assert.Equal([Path.GetFullPath(Path.Combine(_dir, "id_ed25519"))], defaults);
    }

    [Fact]
    public void HasUsableKey_RequiresTheFileToExist()
    {
        var path = Path.Combine(_dir, "id_ed25519");
        Assert.False(SshIdentity.HasUsableKey(path, _dir));
        File.WriteAllText(path, "not-a-key");
        Assert.True(SshIdentity.HasUsableKey(path, _dir));
        Assert.True(SshIdentity.HasUsableKey("", _dir));
        Assert.False(SshIdentity.HasUsableKey("", Path.Combine(_dir, "empty")));
    }

    [Theory]
    [InlineData("Permission denied (password).", true)]
    [InlineData("Permission denied (publickey).", true)]
    [InlineData("Too many authentication failures", true)]
    [InlineData("Auth fail", true)]
    [InlineData("Connection refused", false)]
    [InlineData("无法绑定本地端口", false)]
    [InlineData("私钥文件不存在：x", true)]
    public void LooksLikeAuthenticationFailure_ClassifiesMessages(string message, bool expected)
    {
        Assert.Equal(expected, SshIdentity.LooksLikeAuthenticationFailure(message));
    }

    [Fact]
    public void MissingCredentialsMessage_MentionsKeyOrPassword()
    {
        Assert.Contains("密码", SshIdentity.MissingCredentialsMessage(""));
        Assert.Contains("不存在", SshIdentity.MissingCredentialsMessage(Path.Combine(_dir, "nope")));
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
