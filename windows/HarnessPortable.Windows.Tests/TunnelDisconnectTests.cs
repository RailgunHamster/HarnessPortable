using System.Reflection;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class TunnelDisconnectTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopOrRestartReturnsWithoutWaitingForCancellationCallbacksOrOpenChannels(bool restart)
    {
        var root = Path.Combine(Path.GetTempPath(), "hp-disconnect", Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        var profiles = new ProfileStore(Path.Combine(root, "profiles.json"));
        profiles.SaveTunnels([new() { Id="replacement", SshHost="127.0.0.1", SshPort=1, User="fixture", IdentityFile=Path.Combine(root,"no-key") }]);
        var engine = new TunnelEngine(profiles,
            new SecureStore(Path.Combine(root, "secrets.json")), new KnownHostsStore(Path.Combine(root, "known_hosts.json")));
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        using var callbackEntered = new ManualResetEventSlim();
        using var cleanupEntered = new ManualResetEventSlim();
        var cleanupCount = 0;
        var resources = new TunnelResources();
        resources.OwnForward(() => { cleanupEntered.Set(); release.Wait(TimeSpan.FromSeconds(5)); Interlocked.Increment(ref cleanupCount); });
        using var registration = cancellation.Token.Register(() => { callbackEntered.Set(); release.Wait(TimeSpan.FromSeconds(5)); });
        typeof(TunnelEngine).GetField("_cts", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, cancellation);
        typeof(TunnelEngine).GetField("_resources", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(engine, resources);
        engine.State.Set(new("open-tab", Status: TunnelStatus.Connected));
        var stop = Task.Run(() => { if (restart) engine.Start("replacement"); else engine.Stop(); });
        try
        {
            Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(500)));
            await stop;
            if (!restart) Assert.Equal(TunnelStatus.Stopped, engine.State.Current.Status);
            Assert.Equal(restart ? "replacement" : "open-tab", engine.State.Current.ProfileId);
            Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(2)));
            Assert.True(cleanupEntered.Wait(TimeSpan.FromSeconds(2)));
            // A worker finally block and another stop must not re-enter the teardown.
            resources.Dispose();
            engine.Stop();
        }
        finally { release.Set(); }
        Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref cleanupCount) == 1, TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task WorkerExitAndUserStopCloseResourcesOnce()
    {
        var clientClosed = 0;
        var forwardClosed = 0;
        var resources = new TunnelResources();
        resources.OwnClient(() => Interlocked.Increment(ref clientClosed));
        resources.OwnForward(() => Interlocked.Increment(ref forwardClosed));
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(resources.Dispose)));
        Assert.Equal(1, forwardClosed);
        Assert.Equal(1, clientClosed);
    }

    [Fact]
    public void ForwardThatFinishesBindingAfterStopIsStillReleased()
    {
        var resources = new TunnelResources();
        var closed = 0;
        resources.Dispose();
        resources.OwnForward(() => closed++);
        resources.Dispose();
        Assert.Equal(1, closed);
    }

    [Fact]
    public void FailedForwardCleanupStillClosesTheClient()
    {
        var resources = new TunnelResources();
        var clientClosed = false;
        resources.OwnForward(() => throw new IOException("fixture"));
        resources.OwnClient(() => clientClosed = true);
        Assert.Throws<IOException>(resources.Dispose);
        resources.Dispose();
        Assert.True(clientClosed);
    }
}
