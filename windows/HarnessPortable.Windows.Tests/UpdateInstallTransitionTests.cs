using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class UpdateInstallTransitionTests
{
    private sealed class Session(List<string> calls) : IUpdateSplashSession
    {
        private bool _detached;
        public void Detach() { _detached = true; calls.Add("detach"); }
        public void Dispose() => calls.Add(_detached ? "released" : "abort");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyAReadyWindowEnablesSilentInstallAndTheAppExitsAfterScheduling(bool ready)
    {
        var calls = new List<string>();
        await UpdateInstallTransition.RunAsync(
            _ => { calls.Add("download"); return Task.CompletedTask; },
            _ => { calls.Add("launch"); return Task.FromResult<IUpdateSplashSession?>(ready ? new Session(calls) : null); },
            silent => calls.Add(silent ? "silent-install" : "native-install"),
            () => { calls.Add("shutdown"); return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(ready
            ? ["download", "launch", "silent-install", "shutdown", "detach", "released"]
            : new[] { "download", "launch", "native-install", "shutdown" }, calls);
    }

    [Fact]
    public async Task DownloadFailureNeverStartsTheSplashInstallerOrShutdown()
    {
        var calls = new List<string>();
        await Assert.ThrowsAsync<IOException>(() => UpdateInstallTransition.RunAsync(
            _ => Task.FromException(new IOException("fixture")),
            _ => { calls.Add("launch"); return Task.FromResult<IUpdateSplashSession?>(null); },
            _ => calls.Add("install"), () => { calls.Add("shutdown"); return Task.CompletedTask; }, CancellationToken.None));
        Assert.Empty(calls);
    }

    [Theory]
    [InlineData("download")]
    [InlineData("launch")]
    [InlineData("install")]
    [InlineData("shutdown")]
    public async Task CancellationOrFailureClosesAnUncommittedSplash(string failure)
    {
        var calls = new List<string>();
        using var cancellation = new CancellationTokenSource();
        async Task Run() => await UpdateInstallTransition.RunAsync(
            _ => { if (failure == "download") cancellation.Cancel(); return Task.CompletedTask; },
            _ => { if (failure == "launch") cancellation.Cancel(); return Task.FromResult<IUpdateSplashSession?>(new Session(calls)); },
            _ => { calls.Add("install"); if (failure == "install") throw new IOException("fixture"); },
            () => { calls.Add("shutdown"); if (failure == "shutdown") throw new IOException("fixture"); return Task.CompletedTask; }, cancellation.Token);
        if (failure is "download" or "launch") await Assert.ThrowsAnyAsync<OperationCanceledException>(Run);
        else await Assert.ThrowsAsync<IOException>(Run);
        if (failure != "download") Assert.Equal("abort", calls.Last());
        if (failure is "download" or "launch") Assert.DoesNotContain("install", calls);
        if (failure != "shutdown") Assert.DoesNotContain("shutdown", calls);
        Assert.DoesNotContain("detach", calls);
    }

    [Fact]
    public void LaunchArgumentsPreservePathsWithSpacesAndTheRequestedThemeAndVersions()
    {
        var root = Path.Combine(Path.GetTempPath(), "fixture install with spaces");
        var session = Path.Combine(Path.GetTempPath(), "fixture session with spaces");
        var start = UpdateTransitionSplash.CreateStartInfo("fixture.exe", session, root, "2.8.1", "2.8.2", true, 1234);
        string Argument(string name) => start.ArgumentList[start.ArgumentList.IndexOf(name) + 1];
        Assert.Equal(Path.GetFullPath(root), Argument("--root-dir"));
        Assert.Equal(session, Argument("--session-dir"));
        Assert.Equal("Dark", Argument("--theme"));
        Assert.Equal("2.8.1", Argument("--from-version"));
        Assert.Equal("2.8.2", Argument("--to-version"));
        Assert.Equal("1234", Argument("--wait-pid"));
        Assert.False(start.UseShellExecute);
        Assert.True(start.CreateNoWindow);
    }
}
