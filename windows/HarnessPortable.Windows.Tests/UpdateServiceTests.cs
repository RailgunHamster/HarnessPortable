using System.Diagnostics;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class UpdateServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hp-update-tests", Guid.NewGuid().ToString("N"));
    private AppSettingsStore Settings => new(Path.Combine(_directory, "settings.json"));

    [Fact]
    public async Task SynchronousSourceWorkDoesNotBlockCallerOrStartDuplicateChecks()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var service = new UpdateService(Settings, (_, _) =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(TimeSpan.FromSeconds(10));
            throw new IOException("fixture share unavailable");
        }, TimeSpan.FromSeconds(5));

        var watch = Stopwatch.StartNew();
        var check = service.CheckAsync(@"\\fixture\updates");
        try
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), "CheckAsync blocked the calling thread");
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            Assert.False(check.IsCompleted);
            Assert.True(service.Busy);
            await service.CheckAsync(@"\\fixture\updates");
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally { release.Set(); await check.WaitAsync(TimeSpan.FromSeconds(5)); }

        Assert.False(service.Busy);
        Assert.False(service.CanApply);
        Assert.Contains("fixture share unavailable", service.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeoutOrCancellationAllowsRetryAndDiscardsLateResults(bool cancel)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var service = new UpdateService(Settings, (url, _) =>
        {
            if (url.EndsWith("slow"))
            {
                entered.Set();
                try { release.Wait(TimeSpan.FromSeconds(10)); }
                finally { exited.TrySetResult(); }
                throw new IOException("stale result");
            }
            throw new IOException("retry result");
        }, cancel ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(200));

        var notifications = 0;
        service.StateChanged += () => Interlocked.Increment(ref notifications);
        var check = service.CheckAsync(@"\\fixture\slow", cancellation.Token);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(2)));
            if (cancel) cancellation.Cancel();
            await check.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(service.Busy);
            Assert.Contains(cancel ? "已取消" : "超时", service.Status);

            await service.CheckAsync(@"\\fixture\retry").WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Contains("retry result", service.Status);
            var count = Volatile.Read(ref notifications);
            release.Set();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.Delay(100);
            Assert.Contains("retry result", service.Status);
            Assert.Equal(count, Volatile.Read(ref notifications));
        }
        finally { release.Set(); await check.WaitAsync(TimeSpan.FromSeconds(5)); }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
