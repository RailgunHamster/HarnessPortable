namespace HarnessPortable.Windows.Services;

internal interface IUpdateSplashSession : IDisposable
{
    void Detach();
}

/// <summary>The UI and updates.apply both reach this handoff after a verified download.</summary>
internal static class UpdateInstallTransition
{
    internal static async Task RunAsync(
        Func<CancellationToken, Task> download,
        Func<CancellationToken, Task<IUpdateSplashSession?>> launch,
        Action<bool> scheduleInstall,
        Func<Task> shutdown,
        CancellationToken token)
    {
        await download(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        using var splash = await launch(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        // Keep Velopack's native progress window if the custom window could not render.
        scheduleInstall(splash is not null);
        await shutdown().ConfigureAwait(false);
        splash?.Detach();
    }
}
