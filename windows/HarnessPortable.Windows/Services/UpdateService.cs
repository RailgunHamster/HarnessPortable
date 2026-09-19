using Velopack;
using Velopack.Exceptions;
using Velopack.Logging;

namespace HarnessPortable.Windows.Services;

public sealed class UpdateService
{
    private readonly object _gate = new();
    private UpdateInfo? _pending;
    private string _checkedUrl = "";

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

    public UpdateService()
    {
        try
        {
            var manager = new UpdateManager(UpdateSourceFactory.Create(UpdateSourceFactory.DefaultServerUrl));
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
        SetBusy(true, "正在检查更新…");
        try
        {
            var manager = new UpdateManager(UpdateSourceFactory.Create(url));
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
                await ReportFeedWithoutDeploymentAsync(url).ConfigureAwait(false);
                return;
            }

            var edition = manager.IsPortable ? "便携版" : "安装版";
            var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (info is null)
            {
                FlickerLog.Log("update", "no update (current=" + manager.CurrentVersion + ")");
                FinishCheck(
                    pending: null,
                    url,
                    available: null,
                    canApply: false,
                    status: $"当前版本 {manager.CurrentVersion}（{edition}），已是最新",
                    notes: Changelog.ReadEmbedded());
                return;
            }

            var notes = info.TargetFullRelease.NotesMarkdown;
            if (string.IsNullOrWhiteSpace(notes))
            {
                notes = Changelog.ReadEmbedded();
            }

            FlickerLog.Log("update", "available " + info.TargetFullRelease.Version + " (current=" + manager.CurrentVersion + ")");
            FinishCheck(
                pending: info,
                url,
                available: info.TargetFullRelease.Version.ToString(),
                canApply: true,
                status: $"发现新版本 {info.TargetFullRelease.Version}（当前 {manager.CurrentVersion}，{edition}）",
                notes: notes);
        }
        catch (NotInstalledException)
        {
            FlickerLog.Log("update", "NotInstalledException");
            FinishCheck(
                pending: null,
                url,
                available: null,
                canApply: false,
                status: $"当前版本 {CurrentVersion}（{DeploymentLabel}，无法在线更新。请使用 Setup 安装包或便携 zip）",
                notes: Changelog.ReadEmbedded());
        }
        catch (OperationCanceledException)
        {
            FlickerLog.Log("update", "cancelled");
            SetBusy(false, "已取消检查");
        }
        catch (Exception ex)
        {
            FlickerLog.Log("update", "FAILED " + ex.GetType().Name + ": " + ex.Message);
            FinishCheck(
                pending: null,
                url,
                available: null,
                canApply: false,
                status: "检查更新失败：" + ex.Message,
                notes: Changelog.ReadEmbedded());
        }
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
    private async Task ReportFeedWithoutDeploymentAsync(string url)
    {
        try
        {
            var feed = await UpdateSourceFactory.Create(url)
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
                FinishCheck(
                    pending: null,
                    url,
                    available: null,
                    canApply: false,
                    status: $"当前版本 {CurrentVersion}（{DeploymentLabel}），已是最新",
                    notes: Changelog.ReadEmbedded());
                return;
            }

            var notes = string.IsNullOrWhiteSpace(newest.NotesMarkdown)
                ? Changelog.ReadEmbedded()
                : newest.NotesMarkdown;

            FlickerLog.Log("update", "feed has " + newest.Version + " but this build cannot self-update");
            FinishCheck(
                pending: null,
                url,
                available: newest.Version.ToString(),
                canApply: false,
                status: $"最新版本 {newest.Version}（当前 {CurrentVersion}，{DeploymentLabel} 不能自更新：请用 Setup 安装包或便携 zip）",
                notes: notes);
        }
        catch (Exception ex)
        {
            FlickerLog.Log("update", "feed read FAILED " + ex.GetType().Name + ": " + ex.Message);
            FinishCheck(
                pending: null,
                url,
                available: null,
                canApply: false,
                status: $"当前版本 {CurrentVersion}（{DeploymentLabel}，无法在线更新。请使用 Setup 安装包或便携 zip）",
                notes: Changelog.ReadEmbedded());
        }
    }

    private static bool IsNewerThanCurrent(Version candidate) =>
        Version.TryParse(AppVersion.Current, out var installed) && candidate.CompareTo(installed) > 0;

    public async Task ApplyAsync(string? updateUrl, CancellationToken token = default)
    {
        UpdateInfo? pending;
        string url;
        lock (_gate)
        {
            pending = _pending;
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
            }

            if (pending is null)
            {
                return;
            }
        }

        SetBusy(true, "正在下载更新…");
        try
        {
            var manager = new UpdateManager(UpdateSourceFactory.Create(url));
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
