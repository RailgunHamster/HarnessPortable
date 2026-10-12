namespace HarnessPortable.Windows.Services;

public static class GuardedDisposal
{
    public static void Run(Action dispose, Action recover, TimeSpan budget)
    {
        using var completed = new CancellationTokenSource();
        var token = completed.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(budget, token).ConfigureAwait(false);
                if (!token.IsCancellationRequested) recover();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { FlickerLog.Log("webview", "disposal recovery failed: " + ex.GetType().Name); }
        });
        try { dispose(); }
        finally { completed.Cancel(); }
    }
}
