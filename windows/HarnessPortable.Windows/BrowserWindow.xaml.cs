using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace HarnessPortable.Windows;

public partial class BrowserWindow : Window
{
    private const double BallSize = 48;
    private const double ActionHeight = 38 + 6;

    private readonly AppServices _services;
    private readonly bool _isTunnel;
    private readonly TunnelProfile? _profile;
    private readonly string _url;
    private int _lastPort;

    private bool _ballDragging;
    private bool _ballMoved;
    private bool _tunnelWasDown;
    private System.Windows.Point _dragStart;
    private System.Windows.Point _ballStart;
    private bool _coreReady;

    public BrowserWindow(AppServices services, TunnelProfile profile, int localPort)
    {
        _services = services;
        _isTunnel = true;
        _profile = profile;
        _lastPort = localPort > 0 ? localPort : profile.LocalPort;
        _url = $"http://127.0.0.1:{_lastPort}";

        InitializeComponent();
        Title = $"{profile.DisplayName} · Harness Portable";
        ConfigureInitialState();
    }

    public BrowserWindow(AppServices services, string url)
    {
        _services = services;
        _isTunnel = false;
        _url = url;
        _lastPort = 0;

        InitializeComponent();
        Title = $"{ProfileStore.HostOf(url)} · Harness Portable";
        ConfigureInitialState();
    }

    private void ConfigureInitialState()
    {
        ReconnectAction.Visibility = _isTunnel ? Visibility.Visible : Visibility.Collapsed;
        if (_isTunnel)
        {
            _services.Tunnels.StateChanged += OnTunnelStateChanged;
            Closed += (_, _) => _services.Tunnels.StateChanged -= OnTunnelStateChanged;
            ApplyState(_services.Tunnels.GetState(_profile!.Id));
        }

        Loaded += async (_, _) =>
        {
            PositionOverlay();
            await InitializeWebViewAsync();
        };
        SizeChanged += (_, _) => PositionOverlay();
    }

    private void OnTunnelStateChanged(TunnelInfo info)
    {
        if (_profile is null || info.ProfileId != _profile.Id)
        {
            return;
        }

        ApplyState(info);
    }

    private void ApplyState(TunnelInfo info)
    {
        if (!_isTunnel)
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
        OverlayReturnButton.Content = info.Status is TunnelStatus.Stopped or TunnelStatus.Failed
            ? "返回"
            : "取消";
        StatusOverlay.Visibility = Visibility.Visible;
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
            Navigate(_url);
        }
        catch (Exception ex)
        {
            OverlayTitle.Text = "浏览器初始化失败";
            OverlayMessage.Text = ex.Message;
            OverlayReconnectButton.Visibility = Visibility.Collapsed;
            OverlayReturnButton.Content = "返回";
            StatusOverlay.Visibility = Visibility.Visible;
        }
    }

    private void Navigate(string url)
    {
        _lastPort = _isTunnel && url.StartsWith("http://127.0.0.1:")
            ? int.Parse(url.Split(':')[2].Split('/')[0])
            : _lastPort;

        if (_coreReady)
        {
            WebView.CoreWebView2.Navigate(url);
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

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_isTunnel && _profile is not null)
        {
            _services.Tunnels.Stop(_profile.Id);
        }

        Close();
    }

    private void OverlayReconnect_Click(object sender, RoutedEventArgs e) => Reconnect_Click(sender, e);

    private void OverlayReturn_Click(object sender, RoutedEventArgs e) => Back_Click(sender, e);

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
