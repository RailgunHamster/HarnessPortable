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

    [Theory]
    [InlineData("http://winserver:4096", "192.168.0.104", "http://192.168.0.104:4096")]
    [InlineData("http://winserver:4096/", "192.168.0.104", "http://192.168.0.104:4096/")]
    [InlineData("http://winserver:4096/console?x=1#frag", "10.1.2.3", "http://10.1.2.3:4096/console?x=1#frag")]
    [InlineData("https://winserver", "10.1.2.3", "https://10.1.2.3")]
    [InlineData("http://user:pass@winserver:4096/x", "10.1.2.3", "http://user:pass@10.1.2.3:4096/x")]
    [InlineData("http://[::1]:4096/x", "10.1.2.3", null)] // bracketed IPv6 is not located verbatim
    [InlineData("http://winserver:4096", "", null)]
    [InlineData("winserver:4096", "192.168.0.104", null)] // no http(s) scheme
    [InlineData("ftp://winserver:21/x", "192.168.0.104", null)]
    [InlineData("not a url", "192.168.0.104", null)]
    public void ReplaceHost_SwapsOnlyTheHost(string url, string ip, string? expected)
    {
        Assert.Equal(expected, HostResolver.ReplaceHost(url, ip));
    }

    [Theory]
    [InlineData("http://192.168.0.104:4096/")] // already an IP
    [InlineData("http://[2001:db8::1]:4096/")] // already an IPv6 literal
    [InlineData("ftp://winserver:21")] // non-http scheme
    [InlineData("not a url")]
    public async Task ResolveUrlAsync_NonResolvableInput_ReturnsNullWithoutLookup(string url)
    {
        Assert.Null(await HostResolver.ResolveUrlAsync(url));
    }
}
