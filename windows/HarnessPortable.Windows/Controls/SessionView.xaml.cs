using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HarnessPortable.Windows.Controls;

public partial class SessionView : System.Windows.Controls.UserControl
{
    private const double BallSize = 48;
    private const double ActionHeight = 36 + 6;

    private readonly AppServices _services;
    private readonly bool _isTunnel;
    private readonly TunnelProfile? _profile;
    private readonly string _url;
    private int _lastPort;

    private bool _ballDragging;
    private bool _ballMoved;
    private System.Windows.Point _dragStart;
    private System.Windows.Point _ballStart;
    private bool _tunnelWasDown;
    private bool _coreReady;
    private bool _initialized;
    private bool _shutdown;
    private string? _customLabel;
    private ulong _authCheckedNavId;
    private string? _lastAuthNav;

    public event Action? CloseRequested;
    public event Action<string>? TitleChanged;
    public event Action? FullScreenToggleRequested;
    public event Action? EscapeRequested;

    public string? ProfileId => _profile?.Id;
    public bool IsTunnel => _isTunnel;
    public string? DirectUrl => _isTunnel ? null : _url;
    public int LocalPort => _lastPort;
    public string SessionTitle { get; private set; } = "";
    public bool AppFullScreenActive { get; set; }
    public string? CustomLabel => _customLabel;

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
        ReconnectAction.Visibility = _isTunnel ? Visibility.Visible : Visibility.Collapsed;
        DisconnectAction.Visibility = _isTunnel ? Visibility.Visible : Visibility.Collapsed;

        if (_isTunnel)
        {
            _services.Tunnels.StateChanged += OnTunnelStateChanged;
            ApplyState(_services.Tunnels.GetState(_profile!.Id));
        }

        Loaded += async (_, _) =>
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            PositionOverlay();
            await InitializeWebViewAsync();
        };

        SizeChanged += (_, _) => PositionOverlay();
    }

    public void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;

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
        WebView.Dispatcher.InvokeAsync(
            () =>
            {
                try
                {
                    WebView.Dispose();
                }
                catch
                {
                    // Ignore.
                }
            },
            DispatcherPriority.ApplicationIdle);
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
            Dispatcher.BeginInvoke(() => ApplyState(info));
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

        FlickerLog.Log("focus-web", "FocusWebView queued (" + LogTag + ")");
        Dispatcher.BeginInvoke(DispatcherPriority.Input, FocusWebViewCore);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, FocusWebViewCore);
    }

    private void FocusWebViewCore()
    {
        if (_shutdown)
        {
            return;
        }

        // While the auth prompt is up, the TextBox must keep focus; pulling
        // it back into the page would cancel pasting/IME mid-entry.
        if (AuthOverlay.Visibility == Visibility.Visible)
        {
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

    private string LogTag => _isTunnel ? "tunnel:" + (_profile?.Id ?? "?") : "direct:" + _url;

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
        if (!_isTunnel || _profile is null)
        {
            return;
        }

        FlickerLog.Log("tunnel-state", info.Status + " port=" + info.LocalPort + " lastPort=" + _lastPort);

        if (info.Status == TunnelStatus.Connected)
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
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

        OverlayTitle.Text = info.Status switch
        {
            TunnelStatus.Failed => "连接失败",
            TunnelStatus.Stopped => "隧道已停止",
            TunnelStatus.Retrying => "隧道中断，正在重连…",
            _ => "正在连接…",
        };
        OverlayMessage.Text = info.Message ?? "";
        OverlayReconnectButton.Visibility = info.Status is TunnelStatus.Failed or TunnelStatus.Retrying
            ? Visibility.Visible
            : Visibility.Collapsed;
        OverlayReturnButton.Content = "关闭标签";
        StatusOverlay.Visibility = Visibility.Visible;

        SetTitle(info.Status switch
        {
            TunnelStatus.Failed => $"✕ {_customLabel ?? _profile.DisplayName}",
            TunnelStatus.Stopped => $"○ {_customLabel ?? _profile.DisplayName}",
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
        try
        {
            WebView.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = Path.Combine(AppPaths.DataDirectory, "WebView2"),
            };

            await WebView.EnsureCoreWebView2Async();
            _coreReady = true;
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;

            var core = WebView.CoreWebView2;
            WebView.GotFocus += (_, _) => FlickerLog.Log("webview", "GotFocus (" + LogTag + ")");
            WebView.LostFocus += (_, _) => FlickerLog.Log("webview", "LostFocus (" + LogTag + ")");
            core.NavigationStarting += (_, e) => FlickerLog.Log("webview-nav", "start " + e.Uri + " (" + LogTag + ")");
            core.NavigationCompleted += async (_, e) =>
            {
                FlickerLog.Log("webview-nav", "complete navId=" + e.NavigationId + " ok=" + e.IsSuccess + " (" + LogTag + ")");
                await CheckAuthRequiredAsync(e.NavigationId);
            };

            WebView.PreviewKeyDown += OnWebViewPreviewKeyDown;
            Navigate(await ResolveInitialUrlAsync());
        }
        catch (Exception ex)
        {
            OverlayTitle.Text = "浏览器初始化失败";
            OverlayMessage.Text = ex.Message;
            OverlayReconnectButton.Visibility = Visibility.Collapsed;
            OverlayReturnButton.Content = "关闭标签";
            StatusOverlay.Visibility = Visibility.Visible;
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
    /// </summary>
    private async Task<string> ResolveDirectUrlAsync()
    {
        if (_isTunnel)
        {
            return _url;
        }

        var resolved = await HostResolver.ResolveUrlAsync(_url);
        return resolved ?? _url;
    }

    private void Navigate(string url)
    {
        FlickerLog.Log("navigate", MaskUrl(url) + " (" + LogTag + ")");

        if (_isTunnel && url.StartsWith("http://127.0.0.1:"))
        {
            var portPart = url.Split(':')[2];
            _lastPort = int.Parse(portPart.Split('/')[0]);
        }

        if (_coreReady)
        {
            WebView.CoreWebView2.Navigate(url);
        }
    }

    /// <summary>Never write token query strings into the debug log.</summary>
    private static string MaskUrl(string url)
    {
        var at = url.IndexOf('?');
        return at >= 0 ? url[..at] + "?…" : url;
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
        if (_shutdown || AuthOverlay.Visibility == Visibility.Visible)
        {
            return;
        }

        // One check per navigation; redirect chains share an id.
        if (navigationId == _authCheckedNavId)
        {
            return;
        }

        _authCheckedNavId = navigationId;

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

        return _isTunnel
            ? $"http://127.0.0.1:{_lastPort}{pathAndQuery}"
            : _url.TrimEnd('/') + pathAndQuery;
    }

    private async void AuthOpen_Click(object sender, RoutedEventArgs e)
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

        // Direct sessions may use machine names the embedded resolver cannot
        // resolve; reuse the same resolution as the initial navigation.
        if (!_isTunnel)
        {
            target = await HostResolver.ResolveUrlAsync(target) ?? target;
        }

        FlickerLog.Log("webview-auth", "auth url open: " + MaskUrl(target) + " (" + LogTag + ")");
        Navigate(target);
    }

    private void AuthCancel_Click(object sender, RoutedEventArgs e)
    {
        AuthOverlay.Visibility = Visibility.Collapsed;
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
                if (AppFullScreenActive)
                {
                    e.Handled = true;
                    EscapeRequested?.Invoke();
                }

                break;
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_coreReady)
        {
            WebView.CoreWebView2.Reload();
        }
    }

    /// <summary>
    /// Opens the paste overlay on demand, so the manual web login stays
    /// reachable even when the rejection page is never detected.
    /// </summary>
    private void AuthAction_Click(object sender, RoutedEventArgs e) => ShowAuthOverlay();

    private void Reconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_isTunnel && _profile is not null)
        {
            _services.Tunnels.Start(_profile.Id);
        }
    }

    private void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        if (_isTunnel && _profile is not null)
        {
            _services.Tunnels.Stop(_profile.Id);
        }
    }

    private void OverlayReconnect_Click(object sender, RoutedEventArgs e) => Reconnect_Click(sender, e);

    private void OverlayReturn_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void FloatBall_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _ballDragging = true;
        _ballMoved = false;
        _dragStart = e.GetPosition(OverlayCanvas);
        _ballStart = new System.Windows.Point(Canvas.GetLeft(FloatBall), Canvas.GetTop(FloatBall));
        FloatBall.CaptureMouse();
        e.Handled = true;
    }

    private void FloatBall_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_ballDragging)
        {
            return;
        }

        var current = e.GetPosition(OverlayCanvas);
        var dx = current.X - _dragStart.X;
        var dy = current.Y - _dragStart.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > 3)
        {
            _ballMoved = true;
        }

        var maxX = Math.Max(0, OverlayCanvas.ActualWidth - BallSize);
        var maxY = Math.Max(0, OverlayCanvas.ActualHeight - BallSize);
        var x = Math.Clamp(_ballStart.X + dx, 0, maxX);
        var y = Math.Clamp(_ballStart.Y + dy, 0, maxY);

        Canvas.SetLeft(FloatBall, x);
        Canvas.SetTop(FloatBall, y);
        PositionActions(x, y);
    }

    private void FloatBall_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_ballDragging)
        {
            return;
        }

        _ballDragging = false;
        FloatBall.ReleaseMouseCapture();
        e.Handled = true;

        if (!_ballMoved)
        {
            ActionsPanel.Visibility = ActionsPanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
            FloatBallText.Text = ActionsPanel.Visibility == Visibility.Visible ? "✕" : "☰";
            PositionOverlay();
        }
    }

    private void PositionOverlay()
    {
        var width = OverlayCanvas.ActualWidth;
        var height = OverlayCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var x = Canvas.GetLeft(FloatBall);
        var y = Canvas.GetTop(FloatBall);
        if (double.IsNaN(x) || x < 0 || x > width - BallSize)
        {
            x = width - BallSize - 10;
        }

        if (double.IsNaN(y) || y < 0 || y > height - BallSize)
        {
            y = 10;
        }

        Canvas.SetLeft(FloatBall, x);
        Canvas.SetTop(FloatBall, y);
        PositionActions(x, y);
    }

    private void PositionActions(double ballX, double ballY)
    {
        // Refresh and the auth entry are always visible; reconnect/disconnect
        // only exist for tunnel sessions.
        var buttonCount = _isTunnel ? 4 : 2;
        var panelHeight = buttonCount * ActionHeight;
        var down = ballY + BallSize + panelHeight <= OverlayCanvas.ActualHeight;
        Canvas.SetLeft(ActionsPanel, ballX);
        Canvas.SetTop(ActionsPanel, down ? ballY + BallSize + 6 : ballY - panelHeight - 6);
    }
}
