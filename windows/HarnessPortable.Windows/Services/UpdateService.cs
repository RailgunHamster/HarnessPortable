using Velopack;
using Velopack.Exceptions;

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

    public async Task CheckAsync(string? updateUrl, CancellationToken token = default)
    {
        var url = UpdateSourceFactory.Normalize(updateUrl);
        SetBusy(true, "正在检查更新…");
        try
        {
            var manager = new UpdateManager(UpdateSourceFactory.Create(url));
            if (!manager.IsInstalled)
            {
                FinishCheck(
                    pending: null,
                    url,
                    available: null,
                    canApply: false,
                    status: $"当前版本 {CurrentVersion}（开发运行，未通过安装包启动，无法在线更新）",
                    notes: Changelog.ReadEmbedded());
                return;
            }

            var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (info is null)
            {
                FinishCheck(
                    pending: null,
                    url,
                    available: null,
                    canApply: false,
                    status: $"当前版本 {manager.CurrentVersion}，已是最新",
                    notes: Changelog.ReadEmbedded());
                return;
            }

            var notes = info.TargetFullRelease.NotesMarkdown;
            if (string.IsNullOrWhiteSpace(notes))
            {
                notes = Changelog.ReadEmbedded();
            }

            FinishCheck(
                pending: info,
                url,
                available: info.TargetFullRelease.Version.ToString(),
                canApply: true,
                status: $"发现新版本 {info.TargetFullRelease.Version}（当前 {manager.CurrentVersion}）",
                notes: notes);
        }
        catch (NotInstalledException)
        {
            FinishCheck(
                pending: null,
                url,
                available: null,
                canApply: false,
                status: $"当前版本 {CurrentVersion}（未安装 Velopack 包，无法在线更新）",
                notes: Changelog.ReadEmbedded());
        }
        catch (OperationCanceledException)
        {
            SetBusy(false, "已取消检查");
        }
        catch (Exception ex)
        {
            FinishCheck(
                pending: null,
                url,
                available: null,
                canApply: false,
                status: "检查更新失败：" + ex.Message,
                notes: Changelog.ReadEmbedded());
        }
    }

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
            SetBusy(false, "已取消更新");
        }
        catch (Exception ex)
        {
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
