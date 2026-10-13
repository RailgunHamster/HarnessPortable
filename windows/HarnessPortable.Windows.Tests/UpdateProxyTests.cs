using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;
using Velopack.Logging;

namespace HarnessPortable.Windows.Tests;

public sealed class UpdateProxyTests
{
    [Fact]
    public void OldSettingsDefaultToDirectAndRoundTripNewPreference()
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{}")!;
        Assert.False(old.UpdateProxyEnabled);
        Assert.Equal("localhost:7890", old.UpdateProxyAddress);
        old.UpdateProxyEnabled = true; old.UpdateProxyAddress = "127.0.0.1:8080";
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(old))!;
        Assert.Equal(UpdateProxyOptions.From(old), UpdateProxyOptions.From(restored));
    }

    [Theory]
    [InlineData("localhost:7890", true)]
    [InlineData(" http://127.0.0.1:80/ ", true)]
    [InlineData("[::1]:7890", true)]
    [InlineData("localhost", false)]
    [InlineData("localhost:0", false)]
    [InlineData("localhost:65536", false)]
    [InlineData("https://localhost:7890", false)]
    [InlineData("socks5://localhost:7890", false)]
    [InlineData("http://user:pass@localhost:7890", false)]
    [InlineData("localhost:7890/path", false)]
    [InlineData("localhost:7890?token=x", false)]
    [InlineData("local host:7890", false)]
    public void ValidateProxyAddress(string address, bool valid) => Assert.Equal(valid, UpdateProxyOptions.TryParseAddress(address, out _));

    [Fact]
    public async Task EnabledProxyCarriesFeedArtifactAndRedirect()
    {
        await using var proxy = new WireServer(request => request.Contains("releases.win.json")
            ? (200, "", "{\"Assets\":[{\"PackageId\":\"HarnessPortable\",\"Version\":\"9.0.0\",\"Type\":\"Full\",\"FileName\":\"test.nupkg\",\"Size\":7}]}")
            : request.Contains("test.nupkg") ? (302, "Location: http://download.invalid/final\r\n", "") : (200, "", "package"));
        var source = UpdateSourceFactory.Create("http://update.invalid", new(true, proxy.Address));
        var feed = await source.GetReleaseFeed(NullVelopackLogger.Instance, "HarnessPortable", "win", null, null);
        var asset = Assert.Single(feed.Assets);
        var path = Path.Combine(Path.GetTempPath(), "hp-proxy-" + Guid.NewGuid() + ".nupkg");
        try
        {
            await source.DownloadReleaseEntry(NullVelopackLogger.Instance, asset, path, _ => { }, CancellationToken.None);
            Assert.Equal("package", await File.ReadAllTextAsync(path));
            Assert.Contains(proxy.Requests, r => r.StartsWith("GET http://update.invalid/releases.win.json"));
            Assert.Contains(proxy.Requests, r => r.StartsWith("GET http://download.invalid/final"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task DisabledProxyIgnoresAddressAndUsesDirectConnection()
    {
        var options = new UpdateProxyOptions(false, "invalid address");
        using var handler = options.CreateHandler(); Assert.False(handler.UseProxy);
        await using var origin = new WireServer(_ => (200, "", "direct"));
        Assert.Equal("direct", await new UpdateFileDownloader(options).DownloadString("http://" + origin.Address, null, 0.1));
        Assert.Contains(origin.Requests, r => r.StartsWith("GET / HTTP/"));
    }

    [Fact]
    public async Task GitHubApiUsesHttpsConnectThroughProxy()
    {
        await using var proxy = new WireServer(_ => (502, "", "test proxy refused tunnel"));
        var source = UpdateSourceFactory.Create(UpdateSourceFactory.GitHubRepoUrl, new(true, proxy.Address));
        await Assert.ThrowsAnyAsync<Exception>(() => source.GetReleaseFeed(NullVelopackLogger.Instance, "HarnessPortable", "win", null, null));
        Assert.Contains(proxy.Requests, r => r.StartsWith("CONNECT api.github.com:443 HTTP/"));
    }

    [Fact]
    public async Task UnavailableProxyDoesNotFallBackToOrigin()
    {
        await using var origin = new WireServer(_ => (200, "", "must not reach origin"));
        var socket = new TcpListener(IPAddress.Loopback, 0); socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port; socket.Stop();
        var downloader = new UpdateFileDownloader(new(true, $"localhost:{port}"));
        await Assert.ThrowsAnyAsync<Exception>(() => downloader.DownloadString("http://" + origin.Address, null, timeout: 0.05));
        Assert.Empty(origin.Requests);
    }

    private sealed class WireServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;
        public ConcurrentQueue<string> Requests { get; } = new();
        public string Address => "127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port;
        public WireServer(Func<string, (int Code, string Headers, string Body)> response)
        {
            _listener.Start();
            _loop = Task.Run(async () => {
                while (!_stop.IsCancellationRequested)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, leaveOpen: true);
                    var request = await reader.ReadLineAsync(_stop.Token) ?? "";
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token))) { }
                    Requests.Enqueue(request);
                    var (code, headers, body) = response(request);
                    var bytes = Encoding.UTF8.GetBytes(body);
                    var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} Test\r\n{headers}Content-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(head, _stop.Token); await stream.WriteAsync(bytes, _stop.Token);
                }
            });
        }
        public async ValueTask DisposeAsync()
        {
            _stop.Cancel(); _listener.Stop();
            try { await _loop; } catch (OperationCanceledException) { } catch (SocketException) { }
            _stop.Dispose();
        }
    }
}
