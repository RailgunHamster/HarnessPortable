using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class SshConfigReaderTests
{
    [Fact]
    public void Parse_ReadsAliasesAndAppliesSupportedOptions()
    {
        const string config = """
            Host tcloud
              HostName 124.220.21.113
              User root
              Port 22
            Host nuc
              HostName nuc11atkc4.tail603dd.ts.net
              User wangyuxin
              Port 4322
            Host tc jd
              HostName 101.43.203.147 # shared target
              User ubuntu
            Host *
              ServerAliveInterval 60
            Host *.example
              HostName ignored.example
            """;

        var hosts = SshConfigReader.Parse(config);

        Assert.Collection(
            hosts,
            tcloud =>
            {
                Assert.Equal("tcloud", tcloud.Alias);
                Assert.Equal("124.220.21.113", tcloud.HostName);
                Assert.Equal("root", tcloud.User);
                Assert.Equal(22, tcloud.Port);
            },
            nuc =>
            {
                Assert.Equal("nuc", nuc.Alias);
                Assert.Equal("nuc11atkc4.tail603dd.ts.net", nuc.HostName);
                Assert.Equal("wangyuxin", nuc.User);
                Assert.Equal(4322, nuc.Port);
            },
            tc =>
            {
                Assert.Equal("tc", tc.Alias);
                Assert.Equal("101.43.203.147", tc.HostName);
                Assert.Equal("ubuntu", tc.User);
                Assert.Equal(22, tc.Port);
            },
            jd =>
            {
                Assert.Equal("jd", jd.Alias);
                Assert.Equal("101.43.203.147", jd.HostName);
                Assert.Equal("ubuntu", jd.User);
                Assert.Equal(22, jd.Port);
            });
    }

    [Fact]
    public void ParseUsesFirstMatchingValueAndSkipsPatterns()
    {
        const string config = """
            Host *
              User global
              Port 2200
            Host target
              HostName target.internal
              User local
              Port 70000
            Host !blocked *.corp
              User wildcard
            Host blocked
              HostName blocked.internal
            """;

        var hosts = SshConfigReader.Parse(config);

        var target = Assert.Single(hosts, host => host.Alias == "target");
        Assert.Equal("target.internal", target.HostName);
        Assert.Equal("global", target.User);
        Assert.Equal(2200, target.Port);
        Assert.DoesNotContain(hosts, host => host.Alias is "*.corp" or "!blocked");
    }

    [Fact]
    public void ParseDefaultsMissingHostNameAndInvalidPort()
    {
        var hosts = SshConfigReader.Parse(
            "Host local\n  User me\n  Port not-a-port\nHost bare\n");

        var bare = Assert.Single(hosts);
        Assert.Equal("bare", bare.Alias);
        Assert.Equal("bare", bare.HostName);
        Assert.Null(bare.User);
        Assert.Equal(22, bare.Port);
    }
}
