using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Diagnostics;

namespace HarnessPortable.Windows.Controls;

public partial class SessionView : System.Windows.Controls.UserControl
{
    private readonly AppServices _services;
    private readonly bool _isTunnel;
    private TunnelProfile? _profile;
    private readonly string _url;
    private int _lastPort;

    private bool _tunnelWasDown;
    private bool _coreReady;
    private bool _initialized;
    private bool _shutdown;
    private bool _webStateReapplied;
    private int _focusClaimPending;
    private string? _customLabel;
    private ulong _authCheckedNavId;
    private string? _lastAuthNav;
    private Process? _browserProcess;
    private bool _browserFailed;
    private bool _recoveringBrowser;
    private bool _pageParked;
    private bool _discardingPage;
    private CoreWebView2ScriptDialogOpeningEventArgs? _scriptDialog;
    private CoreWebView2Deferral? _scriptDialogDeferral;
    private int _pageGeneration;
    private string? _currentUrl;

    public event Action? CloseRequested;
    public event Action<string>? TitleChanged;
    public event Action? FullScreenToggleRequested;
    public event Action? EscapeRequested;
    public event Action? ReconnectRequested;
    public event Action? WorkspaceStateChanged;

    public string? ProfileId => _profile?.Id;
    public bool IsTunnel => _isTunnel;
    public string? DirectUrl => _isTunnel ? null : _url;
    public int LocalPort => _lastPort;
    public string SessionTitle { get; private set; } = "";
    public bool AppFullScreenActive { get; set; }
    public bool PaneMaximized { get; set; }
    public string? CustomLabel => _customLabel;

    /// <summary>
    /// Web state recorded in the layout preset for this tab. Used for the first
    /// navigation only; see <see cref="MergeWebState"/>.
    /// </summary>
    public LayoutWebState? InitialWebState { get; set; }

    /// <summary>
    /// The page's live address, which the <c>dsh-view-state</c> plugin keeps in
    /// step with the selected session and panel geometry. Null before the browser
    /// exists or while the page is blank.
    /// </summary>
    public string? CurrentUrl
    {
        get
        {
            // Workspace saves must not make synchronous COM calls into a
            // browser whose forwarded connection is being torn down.
            return _pageParked ? null : _currentUrl;
        }
    }

    /// <summary>Label shown in the rename dialog: never includes the status dot.</summary>
    public string DisplayLabel => _customLabel
        ?? (_isTunnel ? _profile?.DisplayName : ProfileStore.HostOf(_url))
        ?? "";

    public SessionView(AppServices services, TunnelProfile profile, int localPort)
    {
        _services = services;
        _isTunnel = true;
        _profile = profile;
        _lastPort = localPort > 0 ? localPort : profile.LocalPort;
        _url = $"http://127.0.0.1:{_lastPort}";
        SessionTitle = profile.DisplayName;

        InitializeComponent();
        ConfigureInitialState();
    }

    public SessionView(AppServices services, string url)
    {
        _services = services;
        _isTunnel = false;
        _url = url;
        _lastPort = 0;
        SessionTitle = ProfileStore.HostOf(url);

        InitializeComponent();
        ConfigureInitialState();
    }

    private void ConfigureInitialState()
    {
        if (_isTunnel)
        {
            _services.Tunnels.StateChanged += OnTunnelStateChanged;
            ApplyState(_services.Tunnels.GetState(_profile!.Id));
        }

        Loaded += async (_, _) =>
        {
            LogHostState("Loaded");
            if (_initialized || (_isTunnel && _profile is not null && _services.Tunnels.GetState(_profile.Id).Status != TunnelStatus.Connected))
            {
                return;
            }

            _initialized = true;
            await InitializeWebViewAsync();
        };

        // The WebView2 is an HwndHost: every re-host destroys and recreates its
        // child window, which flashes. Recording Loaded/Unloaded plus the HWND
        // is what tells a re-host storm apart from a purely visual flicker.
        Unloaded += (_, _) => LogHostState("Unloaded");
    }

    private void LogHostState(string what)
    {
        try
        {
            FlickerLog.Log(
                "session",
                what + " handle=" + WebView.Handle.ToInt64().ToString("X") +
                " chain=" + DescribeVisualChain() +
                " (" + LogTag + ")");
        }
        catch
        {
            FlickerLog.Log("session", what + " handle=? (" + LogTag + ")");
        }
    }

    /// <summary>
    /// Visual-ancestor chain of this view, innermost first. AvalonDock's focus
    /// manager only rewrites a document's IsActive when the focused HwndHost
    /// sits under a <c>LayoutDocumentControl</c>, so this chain decides whether
    /// the WebView2 can drive the dock's activation at all.
    /// </summary>
    private string DescribeVisualChain()
    {
        var names = new List<string>();
        DependencyObject? current = this;
        while (current is not null && names.Count < 8)
        {
            names.Add(current.GetType().Name);
            try
            {
                current = VisualTreeHelper.GetParent(current);
            }
            catch
            {
                break;
            }
        }

        return string.Join(">", names);
    }

    public void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        _pageGeneration++;
        CompleteScriptDialog(false, updateVisibility: false);

        if (_isTunnel)
        {
            _services.Tunnels.StateChanged -= OnTunnelStateChanged;
        }

        WebView.PreviewKeyDown -= OnWebViewPreviewKeyDown;

        // Dispose off the interaction path. WebView2 requires disposal on
        // the UI thread, but when the browser process is wedged Dispose can
        // block for a long time; queuing it at idle priority lets the tab
        // close / layout switch return immediately instead of freezing the
        // whole window (close button, taskbar close, everything).
        ScheduleWebViewDisposal();
    }

    /// <summary>
    /// After this budget the watchdog ends the captured browser process to
    /// release a blocked Dispose call. Other tabs offer browser recovery.
    /// </summary>
    private static readonly TimeSpan DisposalBudget = TimeSpan.FromSeconds(5);

    private void ScheduleWebViewDisposal()
    {
        ScheduleBrowserDisposal(WebView, _browserProcess);
    }

    private static void ScheduleBrowserDisposal(WebView2 browser, Process? process)
    {
        browser.Dispatcher.InvokeAsync(() => GuardedDisposal.Run(() =>
        {
            try { browser.Dispose(); }
            catch (Exception ex) { FlickerLog.Log("webview", "dispose failed: " + ex.GetType().Name); }
        }, () =>
        {
            // Abort cannot stop an executing dispatcher operation. Ending the
            // captured app-owned browser process releases its blocked COM call.
            if (process is not null && !process.HasExited)
            {
                FlickerLog.Log("webview", "dispose exceeded the budget; recovering the app browser process");
                process.Kill(entireProcessTree: true);
            }
        }, DisposalBudget), DispatcherPriority.ApplicationIdle);
    }

    private void OnTunnelStateChanged(TunnelInfo info)
    {
        if (_profile is null || info.ProfileId != _profile.Id)
        {
            return;
        }

        if (Dispatcher.CheckAccess())
        {
            ApplyState(info);
        }
        else
        {
            Dispatcher.BeginInvoke(() => ApplyState(_services.Tunnels.GetState(_profile.Id)));
        }
    }

    /// <summary>
    /// Give keyboard focus to the embedded browser after the tab becomes active.
    /// WebView2 is an HwndHost: when the dock pane swaps documents the previously
    /// focused (now hidden) webview loses Win32 focus and the newly shown one
    /// never claims it, so keystrokes — including Ctrl+Tab for the next switch —
    /// go nowhere until the user clicks into the page. Focusing explicitly keeps
    /// the keyboard chain alive across tab switches.
    /// </summary>
    public void FocusWebView()
    {
        if (_shutdown)
        {
            return;
        }

        // AvalonDock activates a document from *any* Win32 focus event inside
        // its HwndHost (FocusElementManager.WindowFocusChanging), and each
        // activation is reported as ActiveContentChanged. Claiming focus once
        // per notification turns a burst of activations into a burst of
        // Focus() calls, each of which fires more focus events. Collapse the
        // burst onto the two dispatcher passes that matter.
        if (Interlocked.CompareExchange(ref _focusClaimPending, 1, 0) == 1)
        {
            FlickerLog.Log("focus-web", "coalesced into the pending claim (" + LogTag + ")");
            return;
        }

        FlickerLog.Log("focus-web", "FocusWebView queued (" + LogTag + ")");
        Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusWebViewCore);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Interlocked.Exchange(ref _focusClaimPending, 0);
            FocusWebViewCore();
        });
    }

    private void FocusWebViewCore()
    {
        if (_shutdown)
        {
            return;
        }

        // While the auth prompt is up, the TextBox must keep focus; pulling
        // it back into the page would cancel pasting/IME mid-entry.
        if (AuthOverlay.Visibility == Visibility.Visible || StatusOverlay.Visibility == Visibility.Visible || ScriptDialogOverlay.Visibility == Visibility.Visible)
        {
            return;
        }

        // A collapsed or not-yet-loaded tab must never be focused: claiming
        // focus inside a hidden HwndHost is what makes two documents fight
        // over Win32 focus.
        if (!IsVisible || !IsLoaded)
        {
            FlickerLog.Log("focus-web", "skip: view not shown (" + LogTag + ")");
            return;
        }

        try
        {
            if (WebView.IsKeyboardFocusWithin)
            {
                FlickerLog.Log("focus-web", "skip: already within (" + LogTag + ")");
                return;
            }

            // Never re-assert focus while the user is interacting with this
            // page. IsKeyboardFocusWithin desyncs from Win32 focus for
            // HwndHost controls, so check the real focus: a Focus() call
            // while the caret sits inside the page (especially during IME
            // composition) cancels the composition and makes the input box
            // flicker. Only reclaim focus when it is genuinely elsewhere.
            if (NativeMethods.FocusInsideWindowTree(WebView.Handle))
            {
                FlickerLog.Log("focus-web", "skip: win32 focus inside webview " + NativeMethods.DescribeFocus());
                return;
            }

            FlickerLog.Log("focus-web", "Focus() called (" + LogTag + ") " + NativeMethods.DescribeFocus());
            WebView.Focus();
        }
        catch
        {
            // The control may not be fully re-attached yet; the next
            // activation retries. Nothing user-visible to report.
        }
    }

    private string LogTag => _isTunnel ? "tunnel:" + (_profile?.Id ?? "?") : "direct:" + DiagnosticUrls.Redact(_url);

    public void SetCustomLabel(string label)
    {
        _customLabel = string.IsNullOrWhiteSpace(label) ? null : label.Trim();

        if (_isTunnel && _profile is not null)
        {
            ApplyState(_services.Tunnels.GetState(_profile.Id));
        }
        else
        {
            SetTitle(_customLabel ?? ProfileStore.HostOf(_url));
        }
    }

    private void ApplyState(TunnelInfo info)
    {
        if (_shutdown || !_isTunnel || _profile is null)
        {
            return;
        }

        FlickerLog.Log("tunnel-state", info.Status + " port=" + info.LocalPort + " lastPort=" + _lastPort);

        if (info.Status == TunnelStatus.Connected)
        {
            _pageParked = false;
            _profile = _services.Profiles.FindTunnel(_profile.Id) ?? _profile;
            if (!_initialized && IsLoaded)
            {
                _initialized = true;
                _ = InitializeWebViewAsync();
            }
            if (_browserFailed) { SetTitle($"● {_customLabel ?? _profile.DisplayName}"); return; }
            StatusOverlay.Visibility = Visibility.Collapsed;
            UpdateBrowserVisibility();
            var restored = _tunnelWasDown;
            _tunnelWasDown = false;

            var port = info.LocalPort > 0 ? info.LocalPort : _lastPort;

            // Prefer the token URL fetched by the engine (NSSM mode): it
            // both logs in fresh sessions and revalidates silently when the
            // cookie is still good (server answers a harmless 303).
            var authTarget = NssmAuthUrlFetcher.RewriteToLocal(info.AuthUrl, port, 0);

            if (port != _lastPort || restored || (authTarget is not null && authTarget != _lastAuthNav))
            {
                FlickerLog.Log("tunnel-state", "reload on restore, port " + _lastPort + " -> " + port +
                    (authTarget is null ? "" : " (auth)"));
                _lastAuthNav = authTarget;
                Navigate(authTarget ?? $"http://127.0.0.1:{port}");
            }

            SetTitle($"● {_customLabel ?? _profile.DisplayName}");
            return;
        }

        _tunnelWasDown = true;
        if (info.Status == TunnelStatus.Stopped) ParkDisconnectedPage();

        OverlayTitle.Text = info.Status switch
        {
            TunnelStatus.Failed => "连接失败",
            TunnelStatus.Idle => "尚未连接",
            TunnelStatus.Stopped => "隧道已停止",
            TunnelStatus.Retrying => "隧道中断，正在重连…",
            _ => "正在连接…",
        };
        OverlayMessage.Text = info.Message ?? (info.Status == TunnelStatus.Idle ? "点击连接以启动此工作区的 SSH 隧道。" : "");
        OverlayReconnectButton.Content = info.Status == TunnelStatus.Idle ? "连接" : "重新连接";
        OverlayReconnectButton.Visibility = info.Status is TunnelStatus.Idle or TunnelStatus.Failed or TunnelStatus.Retrying or TunnelStatus.Stopped
            ? Visibility.Visible
            : Visibility.Collapsed;
        OverlayReturnButton.Content = "关闭标签";
        StatusOverlay.Visibility = Visibility.Visible;
        UpdateBrowserVisibility();

        SetTitle(info.Status switch
        {
            TunnelStatus.Failed => $"✕ {_customLabel ?? _profile.DisplayName}",
            TunnelStatus.Stopped => $"○ {_customLabel ?? _profile.DisplayName}",
            TunnelStatus.Idle => $"○ {_customLabel ?? _profile.DisplayName}",
            TunnelStatus.Retrying => $"↻ {_customLabel ?? _profile.DisplayName}",
            _ => $"… {_customLabel ?? _profile.DisplayName}",
        });
    }

    private void SetTitle(string title)
    {
        SessionTitle = title;
        TitleChanged?.Invoke(title);
    }

    private async Task InitializeWebViewAsync()
    {
        _automationNavigating=false; _automationNavigationSucceeded=null;
        var browser = WebView;
        try
        {
            browser.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(AppPaths.DataDirectory, "WebView2"),
            };

            await browser.EnsureCoreWebView2Async();
            if (_shutdown || !ReferenceEquals(WebView, browser)) return;
            _coreReady = true;
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            var core = WebView.CoreWebView2;
            _browserProcess = Process.GetProcessById(checked((int)core.BrowserProcessId));
            _ = _browserProcess.SafeHandle; // Pin this process identity; never target a reused PID.
            // Keep page dialogs inside this tab; a native modal dialog can
            // remain hidden while the tunnel is being disconnected.
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.ScriptDialogOpening += (_, e) =>
            {
                if (_shutdown || !ReferenceEquals(WebView, browser)) return;
                if (_discardingPage)
                {
                    if (e.Kind == CoreWebView2ScriptDialogKind.Beforeunload) e.Accept();
                    return;
                }
                ShowScriptDialog(e);
            };
            core.ContentLoading += (_, _) =>
            {
                if (ReferenceEquals(WebView, browser) && (!_pageParked || core.Source == "about:blank"))
                    _discardingPage = false;
            };
            core.ProcessFailed += (_, e) =>
            {
                if (_shutdown || !ReferenceEquals(WebView, browser) || e.ProcessFailedKind is not (CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited)) return;
                _browserFailed = true;
                _coreReady = false;
                CompleteScriptDialog(false, updateVisibility: false);
                OverlayTitle.Text = "浏览器连接中断";
                OverlayMessage.Text = "点击恢复网页重新打开当前标签。";
                OverlayReconnectButton.Content = "恢复";
                OverlayReconnectButton.Visibility = Visibility.Visible;
                StatusOverlay.Visibility = Visibility.Visible;
                UpdateBrowserVisibility();
            };
            // Focus here is only ever *observed*: the WebView2 is an HwndHost,
            // so it also takes focus while the pane tears it down and re-hosts
            // it (every tab switch). Reacting to that by activating its document
            // fights the tab the user just clicked, so nothing subscribes to
            // this event — the pane control already marks the selected document
            // active on a real selection change.
            WebView.GotFocus += (_, _) => FlickerLog.Log("webview", "GotFocus (" + LogTag + ")");
            WebView.LostFocus += (_, _) => FlickerLog.Log("webview", "LostFocus (" + LogTag + ")");
            core.NavigationStarting += (_, e) =>
            {
                if (_shutdown || !ReferenceEquals(WebView, browser)) return;
                _automationNavigating=true; _automationNavigationSucceeded=null;
                FlickerLog.Log("webview-nav", "start " + MaskUrl(e.Uri) + " (" + LogTag + ")");
            };
            core.SourceChanged += (_, _) =>
            {
                if (_shutdown || !ReferenceEquals(WebView, browser)) return;
                if (!_pageParked) _currentUrl = core.Source;
                WorkspaceStateChanged?.Invoke();
            };
            core.NavigationCompleted += async (_, e) =>
            {
                if (_shutdown || !ReferenceEquals(WebView, browser)) return;
                _automationNavigating=false; _automationNavigationSucceeded=e.IsSuccess;
                if (_pageParked) return;
                FlickerLog.Log("webview-nav", "complete navId=" + e.NavigationId + " ok=" + e.IsSuccess + " (" + LogTag + ")");
                await CheckAuthRequiredAsync(e.NavigationId);
                await ReapplyWebStateSoonAsync();
            };

            WebView.PreviewKeyDown += OnWebViewPreviewKeyDown;
            if (!_isTunnel || _profile is not null && _services.Tunnels.GetState(_profile.Id).Status == TunnelStatus.Connected)
                Navigate(await ResolveInitialUrlAsync());
        }
        catch (Exception ex)
        {
            if (_shutdown || !ReferenceEquals(WebView, browser)) return;
            OverlayTitle.Text = "浏览器初始化失败";
            OverlayMessage.Text = ex.Message;
            OverlayReconnectButton.Visibility = Visibility.Collapsed;
            OverlayReturnButton.Content = "关闭标签";
            StatusOverlay.Visibility = Visibility.Visible;
            UpdateBrowserVisibility();
        }
    }

    /// <summary>
    /// First navigation target. Tunnel sessions consult the engine state: if
    /// the tunnel is already connected and carries a fetched token URL, open
    /// it directly instead of hitting the bare host:port first.
    /// </summary>
    private Task<string> ResolveInitialUrlAsync()
    {
        if (!_isTunnel)
        {
            return ResolveDirectUrlAsync();
        }

        if (_profile is not null)
        {
            var info = _services.Tunnels.GetState(_profile.Id);
            if (info.Status == TunnelStatus.Connected)
            {
                if (info.LocalPort > 0)
                {
                    _lastPort = info.LocalPort;
                }

                var authTarget = NssmAuthUrlFetcher.RewriteToLocal(info.AuthUrl, _lastPort, 0);
                if (authTarget is not null)
                {
                    _lastAuthNav = authTarget;
                    return Task.FromResult(authTarget);
                }

                return Task.FromResult($"http://127.0.0.1:{_lastPort}");
            }
        }

        return Task.FromResult(_url);
    }

    /// <summary>
    /// Direct sessions can be opened by machine name (http://winserver:4096),
    /// which the embedded browser may not resolve on its own. Resolve the name
    /// to an IP first; only when the name is unknown do we fall back to the
    /// original URL and let the browser's resolver try (previous behavior).
    /// The tab's identity (DirectUrl, layouts, saved list) keeps the name.
    ///
    /// Optional auto-login: when the direct URL is marked "nssm" in settings
    /// and this very machine hosts dsh web under NSSM, the verified token
    /// query is grafted onto the destination — no SSH hop exists for a direct
    /// tab, so the tunnel modes do not apply. Failures fall through to the
    /// plain URL and its 401 paste overlay.
    /// </summary>
    private async Task<string> ResolveDirectUrlAsync()
    {
        if (_isTunnel)
        {
            return _url;
        }

        // The URL hostname is part of TLS and HTTP identity. Let the browser
        // resolve it rather than changing the origin to a resolved IP.
        var destination = _url;

        try
        {
            if (_services.Settings.Load().DirectAuthModes.TryGetValue(_url, out var mode) &&
                mode == LocalNssmAuth.ModeNssm)
            {
                var authenticated = await LocalNssmAuth.TryBuildAuthTargetAsync(destination, CancellationToken.None);
                if (authenticated is not null)
                {
                    FlickerLog.Log("webview-auth", "local nssm auth applied (" + LogTag + ")");
                    return authenticated;
                }
            }
        }
        catch (Exception ex)
        {
            FlickerLog.Log("webview-auth", "local nssm auth skipped: " + ex.Message);
        }

        return destination;
    }

    private void Navigate(string url)
    {
        if (_shutdown || _isTunnel && _profile is not null &&
            _services.Tunnels.GetState(_profile.Id).Status != TunnelStatus.Connected) return;
        url = MergeWebState(url);
        FlickerLog.Log("navigate", MaskUrl(url) + " (" + LogTag + ")");

        if (_isTunnel && url.StartsWith("http://127.0.0.1:"))
        {
            _lastPort = new Uri(url).Port;
        }

        if (_coreReady)
        {
            WebView.CoreWebView2.Navigate(url);
        }
    }

    /// <summary>
    /// Re-attaches the dsh view state (session, sidebar, panel widths) to a
    /// navigation target. What the page currently shows wins over what the preset
    /// recorded, so reloading or reconnecting never throws the user back to a
    /// session they have since switched away from; the recorded state is the
    /// starting point for the tab's first navigation.
    /// </summary>
    private string MergeWebState(string url)
    {
        var state = ViewStateUrls.Extract(CurrentUrl) ?? InitialWebState;
        return state is null ? url : ViewStateUrls.Merge(url, state);
    }

    /// <summary>
    /// The tab's own dsh root without a query string, used for the one re-apply.
    /// </summary>
    private string CleanRootUrl() => _isTunnel ? $"http://127.0.0.1:{_lastPort}" : _url;

    /// <summary>
    /// Puts a preset's recorded view state back after the page has settled.
    ///
    /// dsh's startup URL carries a one-shot token and answers with a redirect to the
    /// clean root, and that redirect drops the query string: state merged into the
    /// token URL never reaches the page, while the plugin reads its parameters at
    /// boot only. So give the page a moment to mirror whatever state it already has,
    /// and if ours never arrived, navigate once to the clean root carrying it. The
    /// auth hand-off is left alone — its own completion re-enters here.
    /// </summary>
    private async Task ReapplyWebStateSoonAsync()
    {
        if (_webStateReapplied || InitialWebState is null || _shutdown || _pageParked)
        {
            return;
        }

        var generation = _pageGeneration;
        try
        {
            await Task.Delay(1500);
        }
        catch
        {
            // shutting down
        }

        if (_webStateReapplied || InitialWebState is null || _shutdown || _pageParked || generation != _pageGeneration)
        {
            return;
        }

        var live = CurrentUrl;

        if (live is not null && live.Contains("token=", StringComparison.OrdinalIgnoreCase))
        {
            // The token hand-off is still in flight; its completion calls us again.
            return;
        }

        _webStateReapplied = true;

        if (!ViewStateUrls.NeedsReapply(live))
        {
            return;
        }

        FlickerLog.Log("view-state", "re-applying recorded state after auth (" + LogTag + ")");
        Navigate(CleanRootUrl());
    }

    /// <summary>Never write token query strings into the debug log.</summary>
    private static string MaskUrl(string url)
    {
        return DiagnosticUrls.Redact(url);
    }

    /// <summary>
    /// Services like `dsh web` require the first request to carry the token
    /// from the URL they print on startup ("dsh web authentication required;
    /// reopen the URL printed by dsh web."). The embedded browser normally
    /// opens the bare host:port, so after the tool updates / restarts the
    /// plain page is rejected. Detect that response and let the user paste
    /// the printed URL so the auth handshake (and its cookie) happens inside
    /// this WebView2 profile — no external browser needed.
    ///
    /// WebView2 reports IsSuccess = false for *every* 4xx response
    /// (https://learn.microsoft.com/dotnet/api/microsoft.web.webview2.core.
    /// corewebview2navigationcompletedeventargs.issuccess), i.e. exactly the
    /// 401 rejection this check exists for. The flag therefore only feeds the
    /// log line above; the decision rests on the served body text.
    /// </summary>
    private async Task CheckAuthRequiredAsync(ulong navigationId)
    {
        if (_shutdown || _pageParked || AuthOverlay.Visibility == Visibility.Visible)
        {
            return;
        }

        // One check per navigation; redirect chains share an id.
        if (navigationId == _authCheckedNavId)
        {
            return;
        }

        _authCheckedNavId = navigationId;
        var generation = _pageGeneration;

        try
        {
            var source = WebView.CoreWebView2.Source;
            if (!source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var json = await WebView.CoreWebView2.ExecuteScriptAsync(
                "document.body ? (document.body.innerText || '').slice(0, 4000) : ''");
            var text = JsonSerializer.Deserialize<string>(json) ?? string.Empty;
            if (_shutdown || _pageParked || generation != _pageGeneration) return;

            if (LooksLikeAuthRequired(text))
            {
                FlickerLog.Log("webview-auth", "auth-required page detected (" + LogTag + ")");
                ShowAuthOverlay();
            }
        }
        catch
        {
            // The page may not be scriptable (crashed renderer, ongoing
            // navigation, ...). Nothing to do; the next navigation retries.
        }
    }

    private static bool LooksLikeAuthRequired(string text) =>
        text.Contains("dsh web authentication required", StringComparison.OrdinalIgnoreCase)
        || (text.Contains("authentication required", StringComparison.OrdinalIgnoreCase)
            && text.Contains("reopen the url", StringComparison.OrdinalIgnoreCase));

    private void ShowAuthOverlay()
    {
        AuthHint.Visibility = Visibility.Collapsed;
        AuthOverlay.Visibility = Visibility.Visible;
        UpdateBrowserVisibility();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => AuthUrlBox.Focus());
    }

    /// <summary>
    /// Turn the pasted input into a URL this session can actually open.
    /// Tunnel sessions rewrite the authority to the local forwarded port and
    /// keep the token query; direct sessions keep the pasted URL as-is.
    /// Accepted inputs: full URL ("http://host:port/?token=..."), a bare
    /// query ("?token=..."), a key=value pair ("token=..."), or the launch
    /// token itself (base64url, so "just the key" also works).
    /// </summary>
    private string? BuildAuthTargetUrl(string input)
    {
        string pathAndQuery;

        if (Uri.TryCreate(input, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            if (_isTunnel)
            {
                pathAndQuery = uri.PathAndQuery;
                return $"http://127.0.0.1:{_lastPort}{pathAndQuery}";
            }

            return input;
        }

        if (input.StartsWith('?'))
        {
            pathAndQuery = "/" + input;
        }
        else if (input.Contains('=') && !input.Contains('/') && !input.Contains(' '))
        {
            pathAndQuery = "/?" + input;
        }
        else if (!input.Contains('?') && !input.Contains('/') && !input.Contains(' '))
        {
            pathAndQuery = "/?token=" + Uri.EscapeDataString(input);
        }
        else
        {
            return null;
        }

        if (_isTunnel) return $"http://127.0.0.1:{_lastPort}{pathAndQuery}";
        if (!Uri.TryCreate(_url, UriKind.Absolute, out var destination) || destination.Scheme is not ("http" or "https")) return null;
        return new UriBuilder(destination) { Query = pathAndQuery.TrimStart('/', '?') }.Uri.AbsoluteUri;
    }

    private void AuthOpen_Click(object sender, RoutedEventArgs e)
    {
        var input = AuthUrlBox.Text.Trim();
        var target = BuildAuthTargetUrl(input);

        if (target is null || !target.Contains('?'))
        {
            AuthHint.Text = "无法识别输入。请粘贴 dsh web 打印的完整 URL，或直接粘贴 token 本身。";
            AuthHint.Visibility = Visibility.Visible;
            return;
        }

        AuthOverlay.Visibility = Visibility.Collapsed;
        UpdateBrowserVisibility();

        FlickerLog.Log("webview-auth", "auth url open: " + MaskUrl(target) + " (" + LogTag + ")");
        Navigate(target);
    }

    private void AuthCancel_Click(object sender, RoutedEventArgs e)
    {
        AuthOverlay.Visibility = Visibility.Collapsed;
        UpdateBrowserVisibility();
    }

    private void AuthUrlBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            AuthOpen_Click(sender, e);
        }
    }

    private void AuthUrlBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        AuthHint.Visibility = Visibility.Collapsed;
    }

    private void OnWebViewPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        switch (e.Key)
        {
            case System.Windows.Input.Key.F11:
                e.Handled = true;
                FullScreenToggleRequested?.Invoke();
                break;

            case System.Windows.Input.Key.Escape:
                if (AppFullScreenActive || PaneMaximized)
                {
                    e.Handled = true;
                    EscapeRequested?.Invoke();
                }

                break;
        }
    }

    public async Task RefreshAsync()
    {
        if (_shutdown) return;
        if (_browserFailed) { await RecoverBrowserAsync(); return; }
        if (_coreReady)
        {
            WebView.CoreWebView2.Reload();
        }
    }

    /// <summary>
    /// Opens the paste overlay on demand, so the manual web login stays
    /// reachable even when the rejection page is never detected.
    /// </summary>
    public void RequestAuthentication()
    {
        if (!_shutdown) ShowAuthOverlay();
    }

    public void RequestReconnect()
    {
        if (_shutdown || !_isTunnel) return;
        if (ReconnectRequested is not null) { ReconnectRequested.Invoke(); return; }
        if (_isTunnel && _profile is not null)
        {
            _services.Tunnels.Start(_profile.Id);
        }
    }

    public void RequestDisconnect()
    {
        if (_shutdown) return;
        if (_isTunnel && _profile is not null)
        {
            _services.Tunnels.Stop(_profile.Id);
        }
    }

    private async void OverlayReconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_browserFailed) await RecoverBrowserAsync();
        else RequestReconnect();
    }

    private void OverlayReturn_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void UpdateBrowserVisibility()
    {
        var visibility = StatusOverlay.Visibility == Visibility.Visible || AuthOverlay.Visibility == Visibility.Visible
            || ScriptDialogOverlay.Visibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
        if (WebView.Visibility != visibility) GuardBrowserOperation(() => WebView.Visibility = visibility);
    }

    private void ParkDisconnectedPage()
    {
        if (_pageParked) return;
        InitialWebState = ViewStateUrls.Extract(CurrentUrl) ?? InitialWebState;
        _pageParked = true;
        _pageGeneration++;
        _currentUrl = null;
        _webStateReapplied = false;
        AuthOverlay.Visibility = Visibility.Collapsed;
        if (!_coreReady || _browserFailed) return;
        _discardingPage = true;
        CompleteScriptDialog(false, updateVisibility: false);
        GuardBrowserOperation(() =>
        {
            WebView.CoreWebView2.Stop();
            // Keep the tab/control and cookies, but stop all page activity.
            WebView.CoreWebView2.Navigate("about:blank");
        });
    }

    private void ShowScriptDialog(CoreWebView2ScriptDialogOpeningEventArgs args)
    {
        CompleteScriptDialog(false, updateVisibility: false);
        _scriptDialog = args;
        _scriptDialogDeferral = args.GetDeferral();
        ScriptDialogTitle.Text = args.Kind switch
        {
            CoreWebView2ScriptDialogKind.Beforeunload => "离开当前网页？",
            CoreWebView2ScriptDialogKind.Confirm => "网页确认",
            CoreWebView2ScriptDialogKind.Prompt => "网页输入",
            _ => "网页提示",
        };
        ScriptDialogMessage.Text = args.Message;
        ScriptDialogSource.Text = Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? "来自网页：" + uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port) : "来自当前网页";
        ScriptPromptBox.Text = args.DefaultText;
        ScriptPromptBox.Visibility = args.Kind == CoreWebView2ScriptDialogKind.Prompt ? Visibility.Visible : Visibility.Collapsed;
        ScriptDialogCancel.Visibility = args.Kind == CoreWebView2ScriptDialogKind.Alert ? Visibility.Collapsed : Visibility.Visible;
        ScriptDialogAccept.Content = args.Kind == CoreWebView2ScriptDialogKind.Beforeunload ? "离开网页" : "确定";
        ScriptDialogOverlay.Visibility = Visibility.Visible;
        UpdateBrowserVisibility();
        if (ScriptPromptBox.Visibility == Visibility.Visible) ScriptPromptBox.Focus(); else ScriptDialogAccept.Focus();
    }

    private void CompleteScriptDialog(bool accept, bool updateVisibility = true)
    {
        var args = _scriptDialog;
        var deferral = _scriptDialogDeferral;
        _scriptDialog = null;
        _scriptDialogDeferral = null;
        var input = ScriptPromptBox.Text;
        ScriptDialogOverlay.Visibility = Visibility.Collapsed;
        if (args is not null && deferral is not null)
        {
            GuardBrowserOperation(() =>
            {
                try
                {
                    if (accept)
                    {
                        if (args.Kind == CoreWebView2ScriptDialogKind.Prompt) args.ResultText = input;
                        args.Accept();
                    }
                }
                finally { deferral.Complete(); }
            });
        }
        if (updateVisibility && !_shutdown) { UpdateBrowserVisibility(); FocusWebView(); }
    }

    private void ScriptDialogAccept_Click(object sender, RoutedEventArgs e) => CompleteScriptDialog(true);
    private void ScriptDialogCancel_Click(object sender, RoutedEventArgs e) => CompleteScriptDialog(false);
    private void ScriptDialog_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            e.Handled = true;
            CompleteScriptDialog(e.Key == Key.Enter);
        }
    }

    private void GuardBrowserOperation(Action operation)
    {
        var process = _browserProcess;
        GuardedDisposal.Run(() =>
        {
            try { operation(); }
            catch (Exception ex) { FlickerLog.Log("webview", "state change failed: " + ex.GetType().Name); }
        }, () =>
        {
            if (process is not null && !process.HasExited)
            {
                FlickerLog.Log("webview", "state change exceeded the budget; recovering the app browser process");
                process.Kill(entireProcessTree: true);
            }
        }, DisposalBudget);
    }

    private async Task RecoverBrowserAsync()
    {
        if (_shutdown || _recoveringBrowser) return;
        _recoveringBrowser = true;
        _pageGeneration++;
        CompleteScriptDialog(false, updateVisibility: false);
        try
        {
            InitialWebState = ViewStateUrls.Extract(CurrentUrl) ?? InitialWebState;
            var oldBrowser = WebView;
            oldBrowser.PreviewKeyDown -= OnWebViewPreviewKeyDown;
            BrowserGrid.Children.Remove(oldBrowser);
            WebView = new WebView2();
            BrowserGrid.Children.Insert(0, WebView);
            ScheduleBrowserDisposal(oldBrowser, _browserProcess);
            _browserProcess = null;
            _coreReady = false;
            _browserFailed = false;
            _discardingPage = false;
            _authCheckedNavId = 0;
            OverlayReconnectButton.Content = "重连";
            StatusOverlay.Visibility = Visibility.Collapsed;
            if (_isTunnel && _profile is not null) ApplyState(_services.Tunnels.GetState(_profile.Id));
            UpdateBrowserVisibility();
            await InitializeWebViewAsync();
        }
        finally { _recoveringBrowser = false; }
    }
}
