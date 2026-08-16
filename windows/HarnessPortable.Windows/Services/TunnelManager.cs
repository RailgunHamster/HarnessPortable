namespace HarnessPortable.Windows.Services;

/// <summary>
/// Owns one <see cref="TunnelEngine"/> per tunnel profile so the desktop app
/// can run several SSH tunnels at the same time. Each engine keeps its own
/// state hub; this class re-publishes their events in a single stream.
/// </summary>
public sealed class TunnelManager
{
    private readonly ProfileStore _profiles;
    private readonly SecureStore _secrets;
    private readonly KnownHostsStore _knownHosts;
    private readonly object _gate = new();
    private readonly Dictionary<string, TunnelEngine> _engines = [];

    public TunnelManager(ProfileStore profiles, SecureStore secrets, KnownHostsStore knownHosts)
    {
        _profiles = profiles;
        _secrets = secrets;
        _knownHosts = knownHosts;
    }

    public event Action<TunnelInfo>? StateChanged;

    public void Start(string profileId)
    {
        var engine = GetOrCreate(profileId);
        engine.Start(profileId);
    }

    public void Stop(string profileId, bool announce = true)
    {
        TunnelEngine? engine;
        lock (_gate)
        {
            _engines.TryGetValue(profileId, out engine);
        }

        engine?.Stop(announce);
    }

    public void StopAll(bool announce = false)
    {
        TunnelEngine[] engines;
        lock (_gate)
        {
            engines = _engines.Values.ToArray();
        }

        foreach (var engine in engines)
        {
            engine.Stop(announce);
        }
    }

    public TunnelInfo GetState(string profileId)
    {
        TunnelEngine? engine;
        lock (_gate)
        {
            _engines.TryGetValue(profileId, out engine);
        }

        return engine?.State.Current ?? new TunnelInfo(profileId, Status: TunnelStatus.Stopped);
    }

    public IEnumerable<TunnelInfo> GetActiveStates()
    {
        TunnelEngine[] engines;
        lock (_gate)
        {
            engines = _engines.Values.ToArray();
        }

        foreach (var engine in engines)
        {
            var state = engine.State.Current;
            if (state.Status is TunnelStatus.Connecting or TunnelStatus.Connected or TunnelStatus.Retrying)
            {
                yield return state;
            }
        }
    }

    private TunnelEngine GetOrCreate(string profileId)
    {
        lock (_gate)
        {
            if (_engines.TryGetValue(profileId, out var existing))
            {
                return existing;
            }

            var engine = new TunnelEngine(_profiles, _secrets, _knownHosts);
            engine.State.StateChanged += info => StateChanged?.Invoke(info);
            _engines[profileId] = engine;
            return engine;
        }
    }
}
