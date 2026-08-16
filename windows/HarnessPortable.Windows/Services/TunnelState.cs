namespace HarnessPortable.Windows.Services;

public enum TunnelStatus
{
    Idle,
    Connecting,
    Connected,
    Retrying,
    Failed,
    Stopped,
}

public sealed record TunnelInfo(
    string? ProfileId = null,
    string? ProfileName = null,
    TunnelStatus Status = TunnelStatus.Idle,
    string? Message = null,
    int LocalPort = 0)
{
    public static TunnelInfo Stopped(string? message = "隧道已停止") => new(Status: TunnelStatus.Stopped, Message: message);
}

/// <summary>
/// Observable state of the SSH tunnel. The engine raises <see cref="StateChanged"/>
/// on the captured UI synchronization context so WPF bindings can consume it
/// without extra dispatching.
/// </summary>
public sealed class TunnelStateHub
{
    private readonly SynchronizationContext? _uiContext;
    private TunnelInfo _current = new();
    private readonly object _lock = new();

    public TunnelStateHub()
    {
        _uiContext = SynchronizationContext.Current;
    }

    public event Action<TunnelInfo>? StateChanged;

    public TunnelInfo Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
    }

    public void Set(TunnelInfo info)
    {
        lock (_lock)
        {
            _current = info;
        }

        if (_uiContext is not null)
        {
            _uiContext.Post(_ => StateChanged?.Invoke(info), null);
        }
        else
        {
            StateChanged?.Invoke(info);
        }
    }
}
