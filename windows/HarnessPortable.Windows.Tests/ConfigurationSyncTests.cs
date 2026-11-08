using System.IO;
using System.Text.Json.Nodes;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;
using HarnessPortable.Testing;

namespace HarnessPortable.Windows.Tests;

public sealed class ConfigurationSyncTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "harness-sync-" + Guid.NewGuid().ToString("N"));
    public ConfigurationSyncTests() { Directory.CreateDirectory(_root); }
    public void Dispose() { Directory.Delete(_root, true); }
    private AppServices Device(string name) => new(Path.Combine(_root, name));
    private static JsonObject Fixture() => JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "shared-configuration-v1.json")))!.AsObject();
    private static TunnelProfile Profile(string id = "qa-1", string name = "测试连接") => new() { Id = id, Name = name, SshHost = "fixture.invalid", User = "fixture", IdentityFile = "C:/private/id_fixture" };
    [Fact] public void CrossPlatformFixtureIsAcceptedAndUnknownSecretsAreRejected()
    {
        var doc = Fixture(); SharedConfiguration.Validate(doc);
        doc["entries"]!["tunnel:shared-fixture-1"]!["password"] = "must-never-sync";
        Assert.Throws<InvalidDataException>(() => SharedConfiguration.Validate(doc));
        doc = Fixture(); doc["entries"]!["tunnel:shared-fixture-1"]!["identityFile"] = "/private/key";
        Assert.Throws<InvalidDataException>(() => SharedConfiguration.Validate(doc));
        doc = Fixture(); doc["version"] = 2; Assert.Throws<InvalidDataException>(() => SharedConfiguration.Validate(doc));
    }
    [Fact] public void ReservedVaultKeysAndCredentialBearingUrlsAreRejected()
    {
        var doc = Fixture(); var item = doc["entries"]!["tunnel:shared-fixture-1"]!.DeepClone(); item["id"] = "#webdav-sync";
        doc["entries"]!.AsObject().Remove("tunnel:shared-fixture-1"); doc["entries"]!["tunnel:#webdav-sync"] = item;
        Assert.Throws<InvalidDataException>(() => SharedConfiguration.Validate(doc));
        doc = Fixture(); var url = "https://user:secret@fixture.invalid/?token=private";
        var direct = doc["entries"]!["direct:https://fixture.invalid/console"]!.DeepClone(); direct["url"] = url;
        doc["entries"]!.AsObject().Remove("direct:https://fixture.invalid/console"); doc["entries"]!["direct:" + url] = direct;
        Assert.Throws<InvalidDataException>(() => SharedConfiguration.Validate(doc));
    }
    [Fact] public void MergeCombinesIndependentEditsAndRequiresAChoiceForDeleteVersusEdit()
    {
        var b = Fixture(); var l = (JsonObject)b.DeepClone(); var r = (JsonObject)b.DeepClone();
        l["entries"]!["shared:appearance"] = "light"; r["entries"]!["windows:closeBehavior"] = "exit";
        var merged = SharedConfiguration.Merge(b, l, r); Assert.Empty(merged.Conflicts);
        Assert.Equal("light", merged.Document["entries"]!["shared:appearance"]!.GetValue<string>());
        Assert.Equal("exit", merged.Document["entries"]!["windows:closeBehavior"]!.GetValue<string>());
        l["entries"]!.AsObject().Remove("tunnel:shared-fixture-1"); r["entries"]!["tunnel:shared-fixture-1"]!["name"] = "云端修改";
        Assert.Contains("tunnel:shared-fixture-1", SharedConfiguration.Merge(b, l, r).Conflicts);
        Assert.Null(SharedConfiguration.Merge(b, l, r, "local").Document["entries"]!["tunnel:shared-fixture-1"]);
        Assert.Equal("云端修改", SharedConfiguration.Merge(b, l, r, "remote").Document["entries"]!["tunnel:shared-fixture-1"]!["name"]!.GetValue<string>());
    }
    [Fact] public void SnapshotExcludesVaultPathsAndTokensButPreservesForeignPlatformSettings()
    {
        var a = Device("a"); a.Profiles.SaveTunnels([Profile()]); a.Profiles.SaveDirects(["https://name:direct-secret@fixture.invalid/page?token=web-secret#private"]);
        a.Secrets.SetPassword("qa-1", "ssh-secret"); a.Secrets.SetAuthInput("qa-1", "web-secret");
        var b = Fixture(); var snapshot = a.Sync.Snapshot(b); var text = snapshot.ToJsonString();
        foreach (var secret in new[] { "ssh-secret", "web-secret", "direct-secret", "identityFile", "C:/private" }) Assert.DoesNotContain(secret, text);
        Assert.True(snapshot["entries"]!["android:browserFullscreen"]!.GetValue<bool>());
        Assert.NotNull(snapshot["entries"]!["direct:https://fixture.invalid/page"]);
    }
    [Fact] public async Task TwoRealDevicesMergeWithoutSharingOrDeletingMatchingLocalCredentials()
    {
        await using var cloud = new WebDavFixture(); var a = Device("a"); var b = Device("b");
        a.Profiles.SaveTunnels([Profile()]); a.Secrets.SetPassword("qa-1", "local-ssh-secret"); a.Secrets.SetAuthInput("qa-1", "local-web-secret");
        b.Profiles.SaveTunnels([Profile("qa-2")]);
        foreach (var device in new[] { a, b }) device.Sync.Configure(new(cloud.Url, "dav-user", false, true), "dav-secret");
        await a.Sync.RunAsync(); Assert.Equal("success", a.Sync.Phase);
        await b.Sync.RunAsync(); Assert.Equal("success", b.Sync.Phase);
        await a.Sync.RunAsync(); Assert.Equal("success", a.Sync.Phase);
        Assert.Equal(2, a.Profiles.LoadTunnels().Count); Assert.Equal(2, b.Profiles.LoadTunnels().Count);
        Assert.Equal("local-ssh-secret", a.Secrets.GetPassword("qa-1")); Assert.Equal("local-web-secret", a.Secrets.GetAuthInput("qa-1")); Assert.False(b.Secrets.HasPassword("qa-1"));
        Assert.Equal("C:/private/id_fixture", a.Profiles.FindTunnel("qa-1")!.IdentityFile); Assert.Equal("", b.Profiles.FindTunnel("qa-1")!.IdentityFile);
        foreach (var secret in new[] { "local-ssh-secret", "local-web-secret", "dav-secret", "identityFile", "C:/private" }) Assert.DoesNotContain(secret, cloud.Document!);
        Assert.Contains(cloud.Requests, q => q.Method == "PUT" && q.Headers.GetValueOrDefault("If-None-Match") == "*");
        Assert.Contains(cloud.Requests, q => q.Method == "PUT" && q.Headers.ContainsKey("If-Match"));
    }
    [Fact] public async Task ConcurrentConflictIsVisibleAndRemoteChoiceDoesNotLoseIndependentEntries()
    {
        await using var cloud = new WebDavFixture(); var a = Device("a"); var b = Device("b");
        a.Profiles.SaveTunnels([Profile()]); a.Sync.Configure(new(cloud.Url, "", false, true)); b.Sync.Configure(new(cloud.Url, "", false, true));
        await a.Sync.RunAsync(); await b.Sync.RunAsync();
        a.Profiles.SaveTunnels([Profile(name: "本机 A")]); b.Profiles.SaveTunnels([Profile(name: "本机 B"), Profile("independent")]);
        await a.Sync.RunAsync(); await b.Sync.RunAsync();
        Assert.Equal("conflict", b.Sync.Phase); Assert.Contains("tunnel:qa-1", b.Sync.Conflicts); Assert.Equal("本机 B", b.Profiles.FindTunnel("qa-1")!.Name);
        await b.Sync.RunAsync("remote"); Assert.Equal("success", b.Sync.Phase);
        Assert.Equal("本机 A", b.Profiles.FindTunnel("qa-1")!.Name); Assert.NotNull(b.Profiles.FindTunnel("independent"));
    }
    [Fact] public async Task ChangedServerIdentityDoesNotReuseExistingPasswordsOrKeyPaths()
    {
        await using var cloud = new WebDavFixture(); var a = Device("a"); a.Profiles.SaveTunnels([Profile()]); a.Secrets.SetPassword("qa-1", "must-stay-local");
        a.Sync.Configure(new(cloud.Url, "", false, true)); await a.Sync.RunAsync();
        var remote = JsonNode.Parse(cloud.Document!)!; remote["entries"]!["tunnel:qa-1"]!["sshHost"] = "other.invalid"; cloud.Document = remote.ToJsonString();
        await a.Sync.RunAsync(); Assert.Equal("success", a.Sync.Phase); Assert.False(a.Secrets.HasPassword("qa-1")); Assert.Equal("", a.Profiles.FindTunnel("qa-1")!.IdentityFile);
    }
    [Fact] public async Task StaleResolutionAndCorruptCloudNeverReplaceLocalConfiguration()
    {
        await using var cloud = new WebDavFixture(); var a = Device("a"); a.Profiles.SaveTunnels([Profile()]); a.Sync.Configure(new(cloud.Url, "", false, true)); await a.Sync.RunAsync();
        var before = File.ReadAllText(Path.Combine(_root, "a", "profiles.json")); cloud.Document = "<html>login required</html>";
        await a.Sync.RunAsync(); Assert.Equal("failed", a.Sync.Phase); Assert.Equal(before, File.ReadAllText(Path.Combine(_root, "a", "profiles.json")));
        await a.Sync.RunAsync("remote"); Assert.Equal("failed", a.Sync.Phase); Assert.Equal(before, File.ReadAllText(Path.Combine(_root, "a", "profiles.json")));
    }
    [Theory] [InlineData("W/\"weak\"")] [InlineData("*")] [InlineData("")]
    public async Task WeakMissingOrWildcardEtagsPreventBlindOverwrite(string etag)
    {
        await using var cloud = new WebDavFixture { Document = SharedConfiguration.Empty().ToJsonString(), ETagOverride = etag, OmitETag = etag.Length == 0 };
        var a = Device("a"); a.Profiles.SaveTunnels([Profile()]); a.Sync.Configure(new(cloud.Url, "", false, true)); await a.Sync.RunAsync();
        Assert.Equal("failed", a.Sync.Phase); Assert.DoesNotContain(cloud.Requests, q => q.Method == "PUT"); Assert.NotNull(a.Profiles.FindTunnel("qa-1"));
    }
    [Fact] public async Task ConditionalWriteRereadsAConcurrentCloudChange()
    {
        await using var cloud = new WebDavFixture(); var a = Device("a"); a.Profiles.SaveTunnels([Profile()]); a.Sync.Configure(new(cloud.Url, "", false, true));
        cloud.BeforeNextPut = () => { var doc = SharedConfiguration.Empty(); doc["entries"]!["android:orientation"] = "portrait"; cloud.Document = doc.ToJsonString(); };
        await a.Sync.RunAsync(); Assert.Equal("success", a.Sync.Phase); Assert.True(cloud.Requests.Count(q => q.Method == "GET") >= 2);
        Assert.Equal("portrait", JsonNode.Parse(cloud.Document!)!["entries"]!["android:orientation"]!.GetValue<string>());
    }
    [Fact] public async Task RedirectDoesNotForwardWebDavCredentials()
    {
        await using var destination = new WebDavFixture(); await using var cloud = new WebDavFixture { Redirect = destination.Url };
        using var client = new WebDavConfigurationClient();
        await Assert.ThrowsAsync<InvalidDataException>(() => client.ReadAsync(new(cloud.Url, "fixture", false, true), "fixture-only-password", default));
        Assert.Empty(destination.Requests);
    }
    [Fact] public void PlainHttpRequiresExplicitConsentAndChangingAnEndpointRequiresCredentials()
    {
        Assert.Throws<InvalidDataException>(() => WebDavConfigurationClient.Folder(new("http://127.0.0.1/dav/")));
        var a = Device("a"); a.Sync.Configure(new("https://one.invalid/dav/", "user"), "secret");
        Assert.Throws<InvalidDataException>(() => a.Sync.Configure(new("https://two.invalid/dav/", "user")));
        a.Sync.Configure(new("https://two.invalid/dav/", "user"), clearPassword: true); Assert.False(a.Sync.Status()["hasPassword"]!.GetValue<bool>());
    }
}
