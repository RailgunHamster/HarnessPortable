using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        if (_isTunnel)
        {
            _services.Tunnels.StateChanged -= OnTunnelStateChanged;
        }

        WebView.PreviewKeyDown -= OnWebViewPreviewKeyDown;

        try
        {
            WebView.Dispose();
        }
        catch
        {
            // Ignore.
        }
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

    private void ApplyState(TunnelInfo info)
    {
        if (!_isTunnel || _profile is null)
        {
            return;
        }

        if (info.Status == TunnelStatus.Connected)
        {
            StatusOverlay.Visibility = Visibility.Collapsed;
            var restored = _tunnelWasDown;
            _tunnelWasDown = false;

            var port = info.LocalPort > 0 ? info.LocalPort : _lastPort;
            if (port != _lastPort || restored)
            {
                Navigate($"http://127.0.0.1:{port}");
            }

            SetTitle($"● {_profile.DisplayName}");
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
            TunnelStatus.Failed => $"✕ {_profile.DisplayName}",
            TunnelStatus.Stopped => $"○ {_profile.DisplayName}",
            TunnelStatus.Retrying => $"↻ {_profile.DisplayName}",
            _ => $"… {_profile.DisplayName}",
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
            WebView.PreviewKeyDown += OnWebViewPreviewKeyDown;
            Navigate(_url);
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

    private void Navigate(string url)
    {
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

            case System.Windows.Input.Key.Home:
            case System.Windows.Input.Key.End:
                e.Handled = true;
                ScrollPageToEdge(e.Key == System.Windows.Input.Key.Home);
                break;
        }
    }

    private async void ScrollPageToEdge(bool top)
    {
        if (!_coreReady)
        {
            return;
        }

        const string script = """
            (function (toTop) {
                function isScrollable(el) {
                    if (!el || el.scrollHeight === undefined) return false;
                    return el.scrollHeight - el.clientHeight > 1;
                }
                var candidates = [];
                var root = document.scrollingElement || document.documentElement;
                if (isScrollable(root)) candidates.push(root);
                var all = document.querySelectorAll('div, main, section, article, ul, body');
                for (var i = 0; i < all.length; i++) {
                    if (isScrollable(all[i])) candidates.push(all[i]);
                }
                var best = null;
                var bestDelta = -1;
                for (var j = 0; j < candidates.length; j++) {
                    var delta = Math.abs(candidates[j].scrollHeight - candidates[j].clientHeight);
                    if (delta > bestDelta) { bestDelta = delta; best = candidates[j]; }
                }
                if (!best) return false;
                best.scrollTo({ top: toTop ? 0 : best.scrollHeight, behavior: 'auto' });
                return true;
            })(arguments[0]);
            """;

        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(script.Replace("arguments[0]", top ? "true" : "false"));
        }
        catch
        {
            // Best effort only.
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_coreReady)
        {
            WebView.CoreWebView2.Reload();
        }
    }

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
        var buttonCount = _isTunnel ? 3 : 2;
        var panelHeight = buttonCount * ActionHeight;
        var down = ballY + BallSize + panelHeight <= OverlayCanvas.ActualHeight;
        Canvas.SetLeft(ActionsPanel, ballX);
        Canvas.SetTop(ActionsPanel, down ? ballY + BallSize + 6 : ballY - panelHeight - 6);
    }
}
