using System.Text.Json;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public class SystemdAuthUrlFetcherTests
{
    [Fact]
    public void PackagedCommand_UsesConfiguredServiceAndQuotesEveryArgument()
    {
        var profile = new TunnelProfile
        {
            AuthMode = TunnelProfile.AuthModeSystemd,
            SystemdUnit = "dsh-web@work.service",
            SystemdScope = "system",
            RemotePort = 4080,
            SystemdLogPath = "/home/me/logs/dsh's stdout.log",
        };
        var command = SystemdAuthUrlFetcher.BuildCommand(profile);
        Assert.StartsWith("sh -c '", command);
        Assert.EndsWith(" sh 'dsh-web@work.service' 'system' 4080 '/home/me/logs/dsh'\"'\"'s stdout.log'", command);
    }

    [Fact]
    public void LegacyProfiles_KeepTheirExistingModeAndGetSystemdDefaults()
    {
        var profile = JsonSerializer.Deserialize<TunnelProfile>("{}")!;
        Assert.Equal(TunnelProfile.AuthModeNssm, profile.AuthMode);
        Assert.Equal("dsh-web.service", profile.SystemdUnit);
        Assert.Equal("user", profile.SystemdScope);
        Assert.Equal("", profile.SystemdLogPath);
    }

    [Fact]
    public void SystemdProfile_RoundTripsThroughProfileStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hp-tests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ProfileStore(Path.Combine(directory, "profiles.json"));
            var profile = new TunnelProfile
            {
                SshHost = "linux", User = "me", AuthMode = TunnelProfile.AuthModeSystemd,
                SystemdUnit = "dsh-web@4080.service", SystemdScope = "system",
                SystemdLogPath = "/var/log/dsh stdout.log",
            };
            store.SaveTunnels([profile]);
            Assert.Equal(profile, Assert.Single(store.LoadTunnels()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
