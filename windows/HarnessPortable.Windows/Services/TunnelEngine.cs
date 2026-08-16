using System.Text.RegularExpressions;
using HarnessPortable.Windows.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Background engine that maintains an SSH local port forward
/// (the desktop equivalent of `ssh -N -L local:remoteHost:remotePort user@host`).
///
/// Host keys: trust-on-first-use via <see cref="KnownHostsStore"/> — the first
/// connection to a host remembers its key; a changed key is rejected.
/// </summary>
public sealed partial class TunnelEngine
{
    private readonly ProfileStore _profiles;
    private readonly SecureStore _secrets;
    private readonly KnownHostsStore _knownHosts;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private SshClient? _client;
    private ForwardedPortLocal? _forward;
    private int _generation;

    private TunnelProfile? _activeProfile;

    public TunnelEngine(ProfileStore profiles, SecureStore secrets, KnownHostsStore knownHosts)
    {
        _profiles = profiles;
        _secrets = secrets;
        _knownHosts = knownHosts;
    }

    public TunnelStateHub State { get; } = new();

    public TunnelProfile? ActiveProfile
    {
        get
        {
            lock (_gate)
            {
                return _activeProfile;
            }
        }
    }

    public void Start(string profileId)
    {
        var profile = _profiles.FindTunnel(profileId);
        if (profile is null)
        {
            State.Set(new TunnelInfo(Status: TunnelStatus.Failed, Message: "隧道配置不存在"));
            return;
        }

        CancellationTokenSource cts;
        int generation;
        lock (_gate)
        {
            generation = ++_generation;
            _activeProfile = profile;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            cts = _cts;
        }

        _ = Task.Run(() => RunAsync(profile, generation, cts.Token));
    }

    public void Stop(bool announce = true)
    {
        lock (_gate)
        {
            _generation++;
            _activeProfile = null;
            _cts?.Cancel();
            _cts = null;
        }

        DisconnectCurrent();

        if (announce)
        {
            State.Set(TunnelInfo.Stopped());
        }
    }

    private void DisconnectCurrent()
    {
        SshClient? client;
        ForwardedPortLocal? forward;
        lock (_gate)
        {
            client = _client;
            forward = _forward;
            _client = null;
            _forward = null;
        }

        try
        {
            forward?.Stop();
        }
        catch
        {
            // Ignore: the session is going away anyway.
        }

        try
        {
            forward?.Dispose();
        }
        catch
        {
            // Ignore.
        }

        try
        {
            client?.Disconnect();
        }
        catch
        {
            // Ignore.
        }

        try
        {
            client?.Dispose();
        }
        catch
        {
            // Ignore.
        }
    }

    private async Task RunAsync(TunnelProfile profile, int generation, CancellationToken token)
    {
        var backoff = TimeSpan.FromSeconds(3);
        var watchdog = StartWatchdog(profile, generation, token);

        try
        {
            while (generation == Volatile.Read(ref _generation) && !token.IsCancellationRequested)
            {
                SshClient? client = null;
                ForwardedPortLocal? forward = null;
                string? hostKeyProblem = null;

                try
                {
                    var password = _secrets.GetPassword(profile.Id);
                    if (string.IsNullOrEmpty(password))
                    {
                        State.Set(new TunnelInfo(profile.Id, profile.DisplayName, TunnelStatus.Failed, "未保存密码"));
                        MarkTerminated(generation);
                        return;
                    }

                    State.Set(new TunnelInfo(profile.Id, profile.DisplayName, TunnelStatus.Connecting, "正在连接…"));

                    var resolution = await HostResolver.ResolveAsync(profile.SshHost, token).ConfigureAwait(false);
                    if (resolution is null)
                    {
                        State.Set(new TunnelInfo(
                            profile.Id, profile.DisplayName, TunnelStatus.Failed,
                            HostResolver.FailureMessage(profile.SshHost)));
                        MarkTerminated(generation);
                        return;
                    }

                    var viaLabel = resolution.Source switch
                    {
                        "tailscale" => $" · Tailscale {resolution.Ip}",
                        "ip" => "",
                        _ => $" · {resolution.Ip}",
                    };

                    var keyboardInteractive = new KeyboardInteractiveAuthenticationMethod(profile.User);
                    keyboardInteractive.AuthenticationPrompt += (_, e) =>
                    {
                        foreach (var prompt in e.Prompts)
                        {
                            prompt.Response = password;
                        }
                    };

                    var connectionInfo = new ConnectionInfo(
                        resolution.Ip,
                        profile.SshPort,
                        profile.User,
                        new PasswordAuthenticationMethod(profile.User, password),
                        keyboardInteractive);

                    connectionInfo.Timeout = TimeSpan.FromSeconds(20);

                    client = new SshClient(connectionInfo);
                    client.HostKeyReceived += (_, e) =>
                    {
                        var decision = _knownHosts.Check(
                            profile.SshHost, profile.SshPort, e.HostKeyName, e.HostKey);

                        if (decision == HostKeyDecision.Changed)
                        {
                            e.CanTrust = false;
                            hostKeyProblem =
                                "服务器主机密钥已改变，拒绝连接（可能存在中间人攻击）";
                        }
                    };
                    client.KeepAliveInterval = TimeSpan.FromSeconds(15);

                    lock (_gate)
                    {
                        if (generation != _generation)
                        {
                            return;
                        }

                        _client = client;
                    }

                    await Task.Run(() => client.Connect(), token).ConfigureAwait(false);

                    // Bind the local listener, walking +0..+9 like the Android app.
                    int bound = -1;
                    Exception? lastBindError = null;
                    for (var delta = 0; delta < 10; delta++)
                    {
                        var candidate = profile.LocalPort + delta;
                        if (candidate > 65535)
                        {
                            break;
                        }

                        var candidateForward = new ForwardedPortLocal(
                            "127.0.0.1", (uint)candidate, profile.RemoteHost, (uint)profile.RemotePort);
                        try
                        {
                            client.AddForwardedPort(candidateForward);
                            candidateForward.Start();
                            forward = candidateForward;
                            bound = candidate;
                            break;
                        }
                        catch (Exception ex)
                        {
                            lastBindError = ex;
                            try
                            {
                                client.RemoveForwardedPort(candidateForward);
                            }
                            catch
                            {
                                // It may not have been registered.
                            }

                            try
                            {
                                candidateForward.Dispose();
                            }
                            catch
                            {
                                // Ignore.
                            }
                        }
                    }

                    if (bound < 0)
                    {
                        throw lastBindError ?? new InvalidOperationException("无法绑定本地端口");
                    }

                    lock (_gate)
                    {
                        if (generation != _generation)
                        {
                            return;
                        }

                        _forward = forward;
                    }

                    backoff = TimeSpan.FromSeconds(3);
                    State.Set(new TunnelInfo(
                        profile.Id,
                        profile.DisplayName,
                        TunnelStatus.Connected,
                        $"127.0.0.1:{bound} → {profile.RemoteHost}:{profile.RemotePort}{viaLabel}",
                        bound));

                    // Monitor: leave the connected state as soon as the session dies.
                    while (generation == Volatile.Read(ref _generation) && client.IsConnected)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), token).ConfigureAwait(false);
                    }

                    if (generation != Volatile.Read(ref _generation) || token.IsCancellationRequested)
                    {
                        return;
                    }

                    State.Set(new TunnelInfo(
                        profile.Id, profile.DisplayName, TunnelStatus.Retrying, "连接中断，正在重连…"));
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (generation != Volatile.Read(ref _generation) || token.IsCancellationRequested)
                    {
                        return;
                    }

                    var message = ex.Message ?? ex.GetType().Name;
                    if (!string.IsNullOrEmpty(hostKeyProblem))
                    {
                        State.Set(new TunnelInfo(
                            profile.Id, profile.DisplayName, TunnelStatus.Failed, hostKeyProblem));
                        MarkTerminated(generation);
                        return;
                    }

                    if (LooksLikeAuthenticationFailure(message))
                    {
                        State.Set(new TunnelInfo(
                            profile.Id, profile.DisplayName, TunnelStatus.Failed, $"认证失败：{message}"));
                        MarkTerminated(generation);
                        return;
                    }

                    State.Set(new TunnelInfo(
                        profile.Id, profile.DisplayName, TunnelStatus.Retrying, message));
                }
                finally
                {
                    try
                    {
                        forward?.Stop();
                    }
                    catch
                    {
                        // Ignore.
                    }

                    try
                    {
                        forward?.Dispose();
                    }
                    catch
                    {
                        // Ignore.
                    }

                    lock (_gate)
                    {
                        if (ReferenceEquals(_forward, forward))
                        {
                            _forward = null;
                        }
                    }

                    try
                    {
                        client?.Disconnect();
                    }
                    catch
                    {
                        // Ignore.
                    }

                    try
                    {
                        client?.Dispose();
                    }
                    catch
                    {
                        // Ignore.
                    }

                    lock (_gate)
                    {
                        if (ReferenceEquals(_client, client))
                        {
                            _client = null;
                        }
                    }
                }

                // Interruptible backoff before the next attempt.
                try
                {
                    await Task.Delay(backoff, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
            }
        }
        finally
        {
            await watchdog.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Marks a run as terminal (resolve failure, auth failure, changed host
    /// key). Increments the generation so the watchdog does not resurrect it.
    /// </summary>
    private void MarkTerminated(int generation)
    {
        lock (_gate)
        {
            if (generation == _generation)
            {
                _generation++;
                _activeProfile = null;
            }
        }
    }

    private async Task StartWatchdog(TunnelProfile profile, int generation, CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var current = State.Current;
        if (generation == Volatile.Read(ref _generation) &&
            current.ProfileId == profile.Id &&
            current.Status != TunnelStatus.Connected &&
            !token.IsCancellationRequested)
        {
            // A connect attempt stalled (vendor power management, wedged
            // network stack): tear the worker down and start over.
            DisconnectCurrent();
            Start(profile.Id);
        }
    }

    private static bool LooksLikeAuthenticationFailure(string message)
    {
        return Regex.IsMatch(message, "auth", RegexOptions.IgnoreCase) ||
               message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("用户名", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("密码", StringComparison.OrdinalIgnoreCase);
    }
}
