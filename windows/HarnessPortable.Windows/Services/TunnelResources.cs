namespace HarnessPortable.Windows.Services;

/// <summary>One attempt owns its cleanup, including resources attached after a concurrent stop.</summary>
internal sealed class TunnelResources : IDisposable
{
    private readonly object _gate = new();
    private bool _closed;
    private Action? _closeClient;
    private Action? _closeForward;

    public void OwnClient(Action close) => Own(close, isForward: false);
    public void OwnForward(Action close) => Own(close, isForward: true);

    private void Own(Action close, bool isForward)
    {
        lock (_gate)
        {
            if (!_closed)
            {
                if (isForward) _closeForward = close;
                else _closeClient = close;
                return;
            }
        }
        // Registration runs on the connection worker, never the UI thread.
        close();
    }

    public void Dispose()
    {
        Action? forward;
        Action? client;
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            forward = _closeForward;
            client = _closeClient;
            _closeForward = _closeClient = null;
        }
        try { forward?.Invoke(); }
        finally { client?.Invoke(); }
    }
}
