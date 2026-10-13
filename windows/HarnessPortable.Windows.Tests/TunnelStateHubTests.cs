using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class TunnelStateHubTests
{
    [Fact]
    public void QueuedStopCannotReplaceNewConnectionState()
    {
        var prior = SynchronizationContext.Current;
        var queue = new QueuedContext();
        try
        {
            SynchronizationContext.SetSynchronizationContext(queue);
            var hub = new TunnelStateHub();
            var seen = new List<TunnelInfo>();
            hub.StateChanged += seen.Add;
            hub.Set(new("one", Status: TunnelStatus.Stopped));
            hub.Set(new("one", Status: TunnelStatus.Connecting));
            hub.Set(new("one", Status: TunnelStatus.Connected));
            queue.Drain();
            Assert.Equal(TunnelStatus.Connected, Assert.Single(seen).Status);
        }
        finally { SynchronizationContext.SetSynchronizationContext(prior); }
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<Action> _queue = new();
        public override void Post(SendOrPostCallback callback, object? state) => _queue.Enqueue(() => callback(state));
        public void Drain() { while (_queue.TryDequeue(out var action)) action(); }
    }
}
