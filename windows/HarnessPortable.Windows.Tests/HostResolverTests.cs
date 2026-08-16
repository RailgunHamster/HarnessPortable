using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class HostResolverTests
{
    [Theory]
    [InlineData("192.168.0.104", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("winserver", false)]
    [InlineData("host.local", false)]
    [InlineData("", false)]
    [InlineData("2001:db8::1", true)]
    public void IsIpLiteral_MatchesAndroidBehavior(string host, bool expected)
    {
        Assert.Equal(expected, HostResolver.IsIpLiteral(host));
    }

    [Theory]
    [InlineData("100.64.0.1", true)]
    [InlineData("100.100.1.2", true)]
    [InlineData("100.127.255.255", true)]
    [InlineData("100.63.0.1", false)]
    [InlineData("100.128.0.1", false)]
    [InlineData("192.168.0.1", false)]
    public void IsTailscaleAddress_MatchesCgnatRange(string ip, bool expected)
    {
        Assert.Equal(expected, HostResolver.IsTailscaleAddress(ip));
    }

    [Fact]
    public async Task ResolveAsync_IpLiteral_ReturnsImmediately()
    {
        var resolution = await HostResolver.ResolveAsync("192.168.0.104");

        Assert.NotNull(resolution);
        Assert.Equal("192.168.0.104", resolution.Ip);
        Assert.Equal("ip", resolution.Source);
    }
}
