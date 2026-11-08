using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using HarnessPortable.Windows.Models;

namespace HarnessPortable.Windows.Services;

public sealed class ConfigurationSyncService
{
    private static string PasswordKey(WebDavOptions options) => "#webdav-sync:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(options.Url + "\n" + options.Username))).ToLowerInvariant();
    private readonly AppServices _app;
    private readonly string _statePath;
    private readonly string _journalPath;
    private readonly WebDavConfigurationClient _client = new();
    private CancellationTokenSource? _cancel;
    private JsonObject? _conflictLocal;
    private JsonObject? _conflictRemote;
    private string? _conflictETag;
    private DispatcherTimer? _periodic, _debounce;
    private bool _applying;
    public bool Busy { get; private set; }
    public string Phase { get; private set; } = "idle";
    public string Message { get; private set; } = "尚未同步";
    public string[] Conflicts { get; private set; } = [];
    public event Action? Changed;
    public ConfigurationSyncService(AppServices app, string directory)
    {
        _app = app;
        _statePath = Path.Combine(directory, "settings-sync.json"); _journalPath = Path.Combine(directory, "settings-sync-rollback.json");
        try { Recover(); } catch { Phase = "failed"; Message = "上次同步未完成，无法恢复本机快照；请保留数据目录中的同步恢复文件。"; }
    }
    private sealed class State
    {
        public State() { }
        public WebDavOptions Options { get; set; } = new();
        public JsonObject Base { get; set; } = SharedConfiguration.Empty();
        public DateTimeOffset? LastSuccess { get; set; }
    }
    private State Load()
    {
        if (!File.Exists(_statePath)) return new();
        var state = JsonSerializer.Deserialize<State>(File.ReadAllText(_statePath)) ?? throw new InvalidDataException("本机同步记录损坏");
        SharedConfiguration.Validate(state.Base); return state;
    }
    public JsonObject Status()
    {
        State state; try { state = Load(); } catch { state = new(); }
        return new() { ["url"] = state.Options.Url, ["username"] = state.Options.Username, ["autoSync"] = state.Options.AutoSync,
            ["allowHttp"] = state.Options.AllowHttp, ["hasPassword"] = _app.Secrets.HasPassword(PasswordKey(state.Options)), ["busy"] = Busy,
            ["phase"] = Phase, ["message"] = Message, ["lastSuccess"] = state.LastSuccess?.ToString("O"),
            ["conflicts"] = new JsonArray(Conflicts.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()) };
    }
    public void Configure(WebDavOptions options, string? password = null, bool clearPassword = false)
    {
        if (Busy) throw new InvalidOperationException("请等待同步完成");
        options = options with { Url = options.Url.Trim(), Username = options.Username.Trim() };
        if (options.Url.Length > 0 || options.AutoSync) _ = WebDavConfigurationClient.Folder(options);
        if ((password?.Length ?? 0) > 8192) throw new InvalidDataException("同步密码过长");
        var state = Load(); var same = state.Options.Url == options.Url && state.Options.Username == options.Username;
        if (!same && password is null && !clearPassword && _app.Secrets.HasPassword(PasswordKey(state.Options)))
            throw new InvalidDataException("更换 WebDAV 地址或账号时，请重填同步密码或勾选清除密码。");
        if (clearPassword) _app.Secrets.ClearPassword(PasswordKey(options));
        else if (password is not null) _app.Secrets.SetPassword(PasswordKey(options), password);
        if (!same) { state.Base = SharedConfiguration.Empty(); state.LastSuccess = null; }
        state.Options = options; JsonFile.Write(_statePath, state);
        Conflicts = []; _conflictLocal = null; Phase = "idle"; Message = "同步设置已保存 · 连接密码始终只保存在本机"; Changed?.Invoke();
    }
    public void Cancel() => _cancel?.Cancel();
    public async Task TestAsync() => await ExecuteAsync(async (state, password, token) =>
    { await _client.TestAsync(state.Options, password, token); Message = "WebDAV 目录可访问；首次同步会验证写入权限。"; });
    public async Task RunAsync(string? choice = null, bool automatic = false)
    {
        if (automatic)
        {
            try { if (Busy || Phase == "conflict" || !Load().Options.AutoSync) return; }
            catch { Phase = "failed"; Message = "本机同步记录损坏，未发起网络请求。"; Changed?.Invoke(); return; }
        }
        await ExecuteAsync(async (state, password, token) =>
        {
            if (choice is not null && (choice is not ("local" or "remote") || _conflictLocal is null)) throw new InvalidDataException("请先同步并查看冲突，再选择本机或云端。");
            var local = Snapshot(state.Base);
            if (choice != null && !SharedConfiguration.Same(local, _conflictLocal)) throw new InvalidDataException("本机配置已改变，请重新同步后确认冲突。");
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var remote = await _client.ReadAsync(state.Options, password, token);
                if (choice != null && (remote.ETag != _conflictETag || !SharedConfiguration.Same(remote.Document, _conflictRemote)))
                    throw new InvalidDataException("云端已改变，请重新同步后确认冲突。");
                var merged = SharedConfiguration.Merge(remote.Missing ? SharedConfiguration.Empty() : state.Base, local, remote.Document, choice);
                if (merged.Conflicts.Length > 0 && choice == null)
                {
                    _conflictLocal = local; _conflictRemote = remote.Document; _conflictETag = remote.ETag;
                    Conflicts = merged.Conflicts; Phase = "conflict"; Message = $"有 {Conflicts.Length} 项两端均有修改，请选择冲突项保留哪一端；其他项目照常合并。"; return;
                }
                if (!SharedConfiguration.Same(local, Snapshot(state.Base))) throw new InvalidDataException("同步期间本机配置已改变，未覆盖本机修改，请重试。");
                if (remote.Missing || !SharedConfiguration.Same(merged.Document, remote.Document))
                {
                    if (!await _client.WriteAsync(state.Options, password, merged.Document, remote.ETag, remote.Missing, token))
                    { if (choice != null) throw new InvalidDataException("云端已改变，请重新确认冲突。"); continue; }
                }
                // After PUT, the cloud receipt is durable even if local edits prevent applying it.
                if (!SharedConfiguration.Same(local, Snapshot(state.Base)))
                    throw new InvalidDataException("上传期间本机配置已改变；本机修改已保留，请重新同步。");
                token.ThrowIfCancellationRequested();
                Apply(merged.Document, state);
                Conflicts = []; _conflictLocal = null; Phase = "success";
                Message = "链接库、收藏分组排序及偏好已同步 · 密码、私钥、登录令牌仍留在本机"; return;
            }
            throw new InvalidDataException("其他设备正在修改云端配置，请稍后重试。");
        });
    }
    private async Task ExecuteAsync(Func<State, string, CancellationToken, Task> action)
    {
        if (Busy) throw new InvalidOperationException("同步正在进行");
        Busy = true; Phase = "syncing"; Message = "正在同步…"; _cancel = new(); Changed?.Invoke();
        try
        {
            if (File.Exists(_journalPath)) throw new InvalidDataException("本机同步恢复尚未完成，请重启应用。");
            var state = Load(); _ = WebDavConfigurationClient.Folder(state.Options);
            await action(state, _app.Secrets.GetPassword(PasswordKey(state.Options)) ?? "", _cancel.Token);
            if (Phase == "syncing") Phase = "success";
        }
        catch (OperationCanceledException) { Phase = "cancelled"; Message = "同步已取消，本机配置未替换；下次同步会重新检查云端。"; }
        catch (Exception error) { Phase = "failed"; Message = error is InvalidDataException ? error.Message : "同步失败，未应用云端配置；请检查网络与 WebDAV 设置。"; }
        finally { Busy = false; _cancel.Dispose(); _cancel = null; Changed?.Invoke(); }
    }
    public JsonObject Snapshot(JsonObject baseline)
    {
        var result = SharedConfiguration.Empty(); var entries = SharedConfiguration.Entries(result);
        foreach (var (key, value) in SharedConfiguration.Entries(baseline))
            if (key.StartsWith("android:") || key.StartsWith("macos:")) entries[key] = value?.DeepClone();
        var config = _app.Profiles.Load(); var settings = _app.Settings.Load();
        var keys = config.Tunnels.Select(p => "tunnel:" + p.Id).Concat(config.Directs.Select(u => "direct:" + u)).ToArray();
        var order = settings.ConnectionOrder.Where(keys.Contains).Concat(keys).Distinct().Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);
        foreach (var p in config.Tunnels)
        {
            var key = "tunnel:" + p.Id;
            var value = new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["sshHost"] = p.SshHost, ["sshPort"] = p.SshPort, ["user"] = p.User,
                ["remoteHost"] = p.RemoteHost, ["remotePort"] = p.RemotePort, ["localPort"] = p.LocalPort, ["authMode"] = p.AuthMode,
                ["systemdUnit"] = p.SystemdUnit, ["systemdScope"] = p.SystemdScope, ["systemdLogPath"] = p.SystemdLogPath };
            Metadata(value, key); entries[key] = value;
        }
        foreach (var url in config.Directs)
        {
            var safe = SharedConfiguration.PortableUrl(url); var key = "direct:" + safe;
            if (entries.ContainsKey(key)) throw new InvalidDataException("直连地址去除登录参数后重复，请使用不同网页路径。");
            var value = new JsonObject { ["url"] = safe }; Metadata(value, "direct:" + url); entries[key] = value;
        }
        entries["shared:appearance"] = settings.Appearance;
        entries["windows:closeBehavior"] = settings.CloseBehavior;
        entries["windows:restoreLastLayoutOnStartup"] = settings.RestoreLastLayoutOnStartup;
        entries["windows:reconnectRestoredWorkspace"] = settings.ReconnectRestoredWorkspace;
        entries["windows:connectionSidebarCollapsed"] = settings.ConnectionSidebarCollapsed;
        SharedConfiguration.Validate(result); return result;
        void Metadata(JsonObject value, string key)
        {
            var pref = settings.Connections.GetValueOrDefault(key) ?? new();
            value["favorite"] = pref.Favorite; value["group"] = pref.Group; value["label"] = pref.Name; value["rank"] = order[key];
        }
    }
    private static bool SameIdentity(TunnelProfile a, TunnelProfile b) => string.Equals(a.SshHost, b.SshHost, StringComparison.OrdinalIgnoreCase) && a.SshPort == b.SshPort && a.User == b.User;
    private void Apply(JsonObject document, State state)
    {
        SharedConfiguration.Validate(document);
        var before = _app.Profiles.Load(); var settings = _app.Settings.Load(); var next = new ProfileStore.Config(); var order = new List<string>();
        var entries = SharedConfiguration.Entries(document);
        foreach (var (key, node) in entries.Where(e => e.Key.StartsWith("tunnel:") || e.Key.StartsWith("direct:")).OrderBy(e => e.Value!["rank"]!.GetValue<int>()).ThenBy(e => e.Key, StringComparer.Ordinal))
        {
            var item = (JsonObject)node!; var localKey = key;
            if (key.StartsWith("tunnel:"))
            {
                var p = item.Deserialize<TunnelProfile>(Automation.AutomationApi.JsonOptions)!;
                var old = before.Tunnels.FirstOrDefault(x => x.Id == p.Id);
                if (old != null && SameIdentity(old, p)) p = p with { IdentityFile = old.IdentityFile };
                next.Tunnels.Add(p);
            }
            else
            {
                var url = item["url"]!.GetValue<string>();
                var retained = before.Directs.FirstOrDefault(x => SharedConfiguration.PortableUrl(x) == url) ?? url;
                next.Directs.Add(retained); localKey = "direct:" + retained;
            }
            var pref = settings.Connections.GetValueOrDefault(localKey) ?? new();
            pref.Favorite = item["favorite"]!.GetValue<bool>(); pref.Group = item["group"]!.GetValue<string>(); pref.Name = item["label"]!.GetValue<string>();
            settings.Connections[localKey] = pref; order.Add(localKey);
        }
        settings.ConnectionOrder = order;
        if (entries["shared:appearance"] is { } appearance) settings.Appearance = appearance.GetValue<string>();
        if (entries["windows:closeBehavior"] is { } close) settings.CloseBehavior = close.GetValue<string>();
        if (entries["windows:restoreLastLayoutOnStartup"] is { } restore) settings.RestoreLastLayoutOnStartup = restore.GetValue<bool>();
        if (entries["windows:reconnectRestoredWorkspace"] is { } reconnect) settings.StartupReconnectPreference = settings.ReconnectRestoredWorkspace = reconnect.GetValue<bool>();
        if (entries["windows:connectionSidebarCollapsed"] is { } sidebar) settings.ConnectionSidebarCollapsed = sidebar.GetValue<bool>();
        JsonFile.Write(_journalPath, new JsonObject { ["profiles"] = JsonSerializer.SerializeToNode(before), ["settings"] = JsonSerializer.SerializeToNode(_app.Settings.Load()), ["state"] = JsonSerializer.SerializeToNode(state) });
        _applying = true;
        try
        {
            foreach (var old in before.Tunnels)
            {
                var current = next.Tunnels.FirstOrDefault(p => p.Id == old.Id);
                if (current == null || !SameIdentity(old, current)) { _app.Secrets.ClearPassword(old.Id); _app.Secrets.ClearAuthInput(old.Id); }
                else if (old.RemoteHost != current.RemoteHost || old.RemotePort != current.RemotePort) _app.Secrets.ClearAuthInput(old.Id);
            }
            _app.Profiles.Save(next); _app.Settings.Save(settings);
            state.Base = document; state.LastSuccess = DateTimeOffset.UtcNow; JsonFile.Write(_statePath, state);
            File.Delete(_journalPath);
            AppTheme.SetPreference(settings.Appearance);
        }
        catch { Recover(); throw; }
        finally { _applying = false; }
    }
    private void Recover()
    {
        if (!File.Exists(_journalPath)) return;
        var journal = JsonNode.Parse(File.ReadAllText(_journalPath))!.AsObject();
        _app.Profiles.Save(journal["profiles"]!.Deserialize<ProfileStore.Config>()!);
        _app.Settings.Save(journal["settings"]!.Deserialize<AppSettings>()!);
        JsonFile.Write(_statePath, journal["state"]!.Deserialize<State>()!); File.Delete(_journalPath);
        Phase = "failed"; Message = "上次同步未完成，已恢复本机配置；缺少的连接密码需在本机补填。";
    }
    public void StartAutoSync(Dispatcher dispatcher)
    {
        if (_periodic != null) return;
        _debounce = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => { _debounce!.Stop(); _ = RunAsync(automatic: true); }, dispatcher); _debounce.Stop();
        _periodic = new DispatcherTimer(TimeSpan.FromMinutes(5), DispatcherPriority.Background, (_, _) => { _ = RunAsync(automatic: true); }, dispatcher);
        void ChangedLocally() { if (!_applying && !Busy) dispatcher.BeginInvoke(() => { if (Phase == "conflict") { Phase = "idle"; _conflictLocal = null; } _debounce.Stop(); _debounce.Start(); }); }
        _app.Profiles.Changed += ChangedLocally; _app.Settings.Changed += ChangedLocally;
        _ = RunAsync(automatic: true);
    }
}
