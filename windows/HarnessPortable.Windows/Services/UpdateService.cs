using Velopack;
using Velopack.Exceptions;
using Velopack.Logging;
using Velopack.Sources;

namespace HarnessPortable.Windows.Services;

public sealed class UpdateService
{
    private readonly object _gate = new();
    private UpdateInfo? _pending;
    private string _checkedUrl = "";
    private UpdateProxyOptions _checkedProxy = new();
    private readonly AppSettingsStore _settings;
    private readonly Func<string, UpdateProxyOptions, IUpdateSource> _createSource;
    private readonly TimeSpan _checkTimeout;
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

    private sealed record CheckResult(UpdateInfo? Pending, string? Available, bool CanApply, string Status, string Notes);

    public event Action? StateChanged;

    public string CurrentVersion { get; } = AppVersion.Current;
    public string Status { get; private set; } = "尚未检查更新";
    public string ChangelogText { get; private set; } = Changelog.ReadEmbedded();
    public string? AvailableVersion { get; private set; }
    public bool CanApply { get; private set; }
    public bool Busy { get; private set; }
    public int Progress { get; private set; }
    public bool IsPackaged { get; }
    public bool IsPortable { get; }

    public string DeploymentLabel =>
        !IsPackaged ? "开发构建 / 旧单文件"
        : IsPortable ? "便携版"
        : "安装版";

    public UpdateService(AppSettingsStore? settings = null)
        : this(settings, UpdateSourceFactory.Create, CheckTimeout) { }

    internal UpdateService(AppSettingsStore? settings, Func<string, UpdateProxyOptions, IUpdateSource> createSource, TimeSpan checkTimeout)
    {
        _settings = settings ?? new AppSettingsStore();
        _createSource = createSource;
        _checkTimeout = checkTimeout;
        try
        {
            // Deployment detection needs only the local locator, never a network share.
            var manager = new UpdateManager(new SimpleFileSource(new System.IO.DirectoryInfo(AppContext.BaseDirectory)));
            IsPackaged = manager.IsInstalled;
            IsPortable = manager.IsPortable;
        }
        catch
        {
            IsPackaged = false;
            IsPortable = false;
        }
    }

    public async Task CheckAsync(string? updateUrl, CancellationToken token = default)
    {
        var url = UpdateSourceFactory.Normalize(updateUrl);
        var proxy = UpdateProxyOptions.From(_settings.Load());
        lock (_gate)
        {
            if (Busy) return;
            _pending = null;
            _checkedProxy = proxy;
            CanApply = false;
            AvailableVersion = null;
            Busy = true;
            Progress = 0;
            Status = "正在检查更新…";
        }
        StateChanged?.Invoke();
        try
        {
            // SimpleFileSource can block synchronously on SMB even though its API
            // returns a Task. Keep source creation and all feed reads off the UI.
            // The worker returns data only, so a late result after timeout/cancel
            // cannot replace the result of a subsequent check.
            var result = await Task.Run(() => ReadCheckResultAsync(url, proxy), token)
                .WaitAsync(_checkTimeout, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            FinishCheck(result.Pending, url, result.Available, result.CanApply, result.Status, result.Notes);
        }
        catch (TimeoutException)
        {
            FlickerLog.Log("update", "check timed out");
            FinishCheck(null, url, null, false, "检查更新超时，请检查更新源后重试", Changelog.ReadEmbedded());
        }
        catch (OperationCanceledException)
        {
            FlickerLog.Log("update", "cancelled");
            SetBusy(false, "已取消检查");
        }
        catch (Exception ex)
        {
            FlickerLog.Log("update", "FAILED " + ex.GetType().Name + ": " + ex.Message);
            FinishCheck(null, url, null, false, "检查更新失败：" + ex.Message, Changelog.ReadEmbedded());
        }
    }

    private async Task<CheckResult> ReadCheckResultAsync(string url, UpdateProxyOptions proxy)
    {
        try
        {
            var source = _createSource(url, proxy);
            var manager = new UpdateManager(source);
            FlickerLog.Log(
                "update",
                "check source=" + MaskUrl(url) +
                " installed=" + manager.IsInstalled +
                " portable=" + manager.IsPortable +
                " current=" + manager.CurrentVersion +
                " appId=" + manager.AppId);
            if (!manager.IsInstalled)
            {
                FlickerLog.Log("update", "not a Velopack deployment (" + DeploymentLabel + "); reading the feed directly");
                return await ReadFeedWithoutDeploymentAsync(source).ConfigureAwait(false);
            }

            var edition = manager.IsPortable ? "便携版" : "安装版";
            var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                FlickerLog.Log("update", "no update (current=" + manager.CurrentVersion + ")");
                return new(null, null, false,
                    $"当前版本 {manager.CurrentVersion}（{edition}），已是最新", Changelog.ReadEmbedded());
            }

            var notes = info.TargetFullRelease.NotesMarkdown;
            if (string.IsNullOrWhiteSpace(notes))
            {
                notes = Changelog.ReadEmbedded();
            }

            FlickerLog.Log("update", "available " + info.TargetFullRelease.Version + " (current=" + manager.CurrentVersion + ")");
            return new(info, info.TargetFullRelease.Version.ToString(), true,
                $"发现新版本 {info.TargetFullRelease.Version}（当前 {manager.CurrentVersion}，{edition}）", notes);
        }
        catch (NotInstalledException)
        {
            FlickerLog.Log("update", "NotInstalledException");
            return new(null, null, false,
                $"当前版本 {CurrentVersion}（{DeploymentLabel}，无法在线更新。请使用 Setup 安装包或便携 zip）", Changelog.ReadEmbedded());
        }
    }

    public void ResetSourceResult()
    {
        if (Busy) return;
        FinishCheck(null, "", null, false, "更新设置已更改，请重新检查", Changelog.ReadEmbedded());
    }

    /// <summary>Never write query strings (tokens) into the debug log.</summary>
    private static string MaskUrl(string url)
    {
        var at = url.IndexOf('?');
        return at >= 0 ? url[..at] + "?…" : url;
    }

    /// <summary>Channel Velopack packs Windows feeds under (releases.win.json).</summary>
    private const string WindowsChannel = "win";

    /// <summary>
    /// A build Velopack does not manage — a plain <c>dotnet publish</c> output or
    /// a copy of one — cannot install anything itself, but it can still answer
    /// the question the button asks ("is there something newer?") by reading the
    /// release feed directly. The user then learns the version and its notes
    /// instead of only being told that updates are unavailable here.
    /// </summary>
    private async Task<CheckResult> ReadFeedWithoutDeploymentAsync(IUpdateSource source)
    {
        try
        {
            var feed = await source
                .GetReleaseFeed(
                    NullVelopackLogger.Instance,
                    appId: "HarnessPortable",
                    channel: WindowsChannel,
                    stagingId: null,
                    latestLocalRelease: null)
                .ConfigureAwait(false);

            var newest = feed.Assets
                .Where(a => a.Type == VelopackAssetType.Full)
                .OrderByDescending(a => a.Version.Version)
                .FirstOrDefault();

            if (newest is null || !IsNewerThanCurrent(newest.Version.Version))
            {
                FlickerLog.Log("update", "feed has no release newer than " + CurrentVersion);
                return new(null, null, false,
                    $"当前版本 {CurrentVersion}（{DeploymentLabel}），已是最新", Changelog.ReadEmbedded());
            }

            var notes = string.IsNullOrWhiteSpace(newest.NotesMarkdown)
                ? Changelog.ReadEmbedded()
                : newest.NotesMarkdown;

            FlickerLog.Log("update", "feed has " + newest.Version + " but this build cannot self-update");
            return new(null, newest.Version.ToString(), false,
                $"最新版本 {newest.Version}（当前 {CurrentVersion}，{DeploymentLabel} 不能自更新：请用 Setup 安装包或便携 zip）", notes);
        }
        catch (Exception ex)
        {
            FlickerLog.Log("update", "feed read FAILED " + ex.GetType().Name + ": " + ex.Message);
            return new(null, null, false, "检查更新失败：" + ex.Message, Changelog.ReadEmbedded());
        }
    }

    private static bool IsNewerThanCurrent(Version candidate) =>
        Version.TryParse(AppVersion.Current, out var installed) && candidate.CompareTo(installed) > 0;

    public async Task ApplyAsync(string? updateUrl, CancellationToken token = default)
    {
        if (Busy) return;
        UpdateInfo? pending;
        string url;
        UpdateProxyOptions proxy;
        lock (_gate)
        {
            pending = _pending;
            proxy = _checkedProxy;
            url = string.IsNullOrWhiteSpace(_checkedUrl)
                ? UpdateSourceFactory.Normalize(updateUrl)
                : _checkedUrl;
        }

        if (pending is null)
        {
            await CheckAsync(url, token).ConfigureAwait(false);
            lock (_gate)
            {
                pending = _pending;
                proxy = _checkedProxy;
            }

            if (pending is null)
            {
                return;
            }
        }

        SetBusy(true, "正在下载更新…");
        try
        {
            var manager = new UpdateManager(UpdateSourceFactory.Create(url, proxy));
            FlickerLog.Log("update", "apply " + pending.TargetFullRelease.Version + " from " + MaskUrl(url));
            await manager.DownloadUpdatesAsync(pending, p =>
            {
                lock (_gate)
                {
                    Progress = p;
                    Status = $"正在下载更新… {p}%";
                }

                StateChanged?.Invoke();
            }, token).ConfigureAwait(false);

            lock (_gate)
            {
                Status = "正在安装并重启…";
                Busy = true;
            }

            StateChanged?.Invoke();
            manager.ApplyUpdatesAndRestart(pending.TargetFullRelease);
        }
        catch (OperationCanceledException)
        {
            FlickerLog.Log("update", "apply cancelled");
            SetBusy(false, "已取消更新");
        }
        catch (Exception ex)
        {
            FlickerLog.Log("update", "apply FAILED " + ex.GetType().Name + ": " + ex.Message);
            SetBusy(false, "更新失败：" + ex.Message);
        }
    }

    private void FinishCheck(
        UpdateInfo? pending,
        string url,
        string? available,
        bool canApply,
        string status,
        string notes)
    {
        lock (_gate)
        {
            _pending = pending;
            _checkedUrl = url;
            AvailableVersion = available;
            CanApply = canApply;
            Status = status;
            ChangelogText = string.IsNullOrWhiteSpace(notes) ? Changelog.ReadEmbedded() : notes;
            Busy = false;
            Progress = 0;
        }

        StateChanged?.Invoke();
    }

    private void SetBusy(bool busy, string status)
    {
        lock (_gate)
        {
            Busy = busy;
            Status = status;
            if (busy)
            {
                Progress = 0;
            }
        }

        StateChanged?.Invoke();
    }
}
