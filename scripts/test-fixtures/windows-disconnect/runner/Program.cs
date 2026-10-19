using System.IO;
using System.Diagnostics;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using HarnessPortable.Windows;
using HarnessPortable.Windows.Controls;
using HarnessPortable.Windows.Models;
using HarnessPortable.Windows.Services;
using Microsoft.Web.WebView2.Wpf;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Environment.SetEnvironmentVariable("HARNESS_PORTABLE_DATA_DIRECTORY", Path.GetFullPath(args[0]));

        var app = new Application(); app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/HarnessPortable;component/Themes/Modern.xaml") }); AppTheme.SetPreference("light");
        var services = new AppServices();
        var a = services.Profiles.FindTunnel("disconnect-a")!;
        var b = services.Profiles.FindTunnel("disconnect-b")!;
        services.Tunnels.Start(a.Id); services.Tunnels.Start(b.Id);
        Wait(() => services.Tunnels.GetState(a.Id).Status == TunnelStatus.Connected && services.Tunnels.GetState(b.Id).Status == TunnelStatus.Connected, "SSH connect");
        var views = new[] {
            new SessionView(services,a,48191) { InitialWebState = new() { SessionId="retained", Sidebar=0 } },
            new SessionView(services,a,48191),
            new SessionView(services,b,48192),
            new SessionView(services,"http://127.0.0.1:45891/")
        };
        var grid = new Grid();
        for (var i=0; i<4; i++) { grid.ColumnDefinitions.Add(new()); Grid.SetColumn(views[i],i); grid.Children.Add(views[i]); }
        var window = new Window { Title="Isolated disconnect lifecycle regression",Width=1100,Height=600,Content=grid };
        var ticks=0;
        var pulse=new DispatcherTimer(TimeSpan.FromMilliseconds(25),DispatcherPriority.Normal,(_,_)=>ticks++,Dispatcher.CurrentDispatcher);
        window.Show();
        try
        {
            Wait(() => views.All(v=>Browser(v).CoreWebView2?.Source.StartsWith("http") == true),"all four pages loaded");
            if (args.Contains("--beforeunload"))
            {
                foreach(var view in views.Take(2))
                {
                    var script = "window.addEventListener('beforeunload',e=>{e.preventDefault();e.returnValue='';});navigator.userActivation.hasBeenActive";
                    var payload = System.Text.Json.JsonSerializer.Serialize(new { expression=script, userGesture=true, returnByValue=true });
                    var activation=Browser(view).CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.evaluate",payload);
                    Wait(()=>activation.IsCompleted,"beforeunload fixture setup");
                    Console.WriteLine("beforeunload fixture: "+activation.GetAwaiter().GetResult());
                }
            }
            CheckDialog(views[0], "confirm('fixture confirm')", false, "false");
            CheckDialog(views[0], "prompt('fixture prompt', 'default')", true, "\"fixture input\"", "fixture input");
            Console.WriteLine("PASS: ordinary confirm cancellation and prompt input");
            var stoppedMilliseconds = new List<double>();
            for(var cycle=0; cycle<3; cycle++)
            {
                if (cycle==0 && args.Contains("--alert"))
                {
                    _ = Browser(views[0]).CoreWebView2.ExecuteScriptAsync("alert('isolated disconnect fixture')");
                    Wait(()=>((FrameworkElement)views[0].FindName("ScriptDialogOverlay")).Visibility==Visibility.Visible,"already-open script alert");
                    Console.WriteLine("fixture: script alert is open before disconnect");
                    if(args.Contains("--capture"))
                    {
                        window.UpdateLayout();
                        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(window);
                        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var capture=File.Create(Path.Combine(Path.GetFullPath(args[0]),"script-dialog.png")); encoder.Save(capture);
                    }
                }
                var before=ticks;
                var watch=Stopwatch.StartNew();
                if (cycle==0) views[0].RequestDisconnect(); else services.Tunnels.Stop(a.Id);
                watch.Stop(); stoppedMilliseconds.Add(watch.Elapsed.TotalMilliseconds);
                Assert(watch.Elapsed < TimeSpan.FromMilliseconds(500),"disconnect blocked the caller");
                Wait(()=>views.Take(2).All(v=>Browser(v).CoreWebView2?.Source=="about:blank"),"open tunnel pages parked");
                Wait(()=> !Listening(48191),"original forwarded port released",5);
                Delay(1900); // The old delayed view-state/auth work must stay cancelled.
                Assert(views.Take(2).All(v=>Browser(v).CoreWebView2?.Source=="about:blank"),"stale callback resumed an offline page");
                Assert(grid.Children.Count==4,"disconnect removed a tab");
                Assert(services.Tunnels.GetState(b.Id).Status==TunnelStatus.Connected,"another SSH tunnel stopped");
                Assert(Browser(views[2]).CoreWebView2.Source.StartsWith("http") && Browser(views[3]).CoreWebView2.Source.StartsWith("http"),"unrelated page changed");
                Assert(ticks>before+5,"UI dispatcher stopped responding");
                Assert(views[0].InitialWebState?.SessionId=="retained","saved view state was lost");
                views[0].RequestReconnect();
                Wait(()=>services.Tunnels.GetState(a.Id).Status==TunnelStatus.Connected,"reconnect");
                Wait(()=>views.Take(2).All(v=>Browser(v).CoreWebView2?.Source.StartsWith("http")==true),"both existing tabs restored");
                Assert(services.Tunnels.GetState(a.Id).LocalPort==48191,"reconnect leaked the original port");
                Console.WriteLine($"cycle {cycle+1}: stop {watch.Elapsed.TotalMilliseconds:F1} ms; 4 tabs retained, 2 parked/restored, other tunnel/direct page live");
            }
            services.Tunnels.StopAll();
            Wait(()=>views.Take(3).All(v=>Browser(v).CoreWebView2?.Source=="about:blank"),"stop all with tabs open");
            Assert(Browser(views[3]).CoreWebView2.Source.StartsWith("http"),"stop all affected direct connection");
            Console.WriteLine("PASS: stop all; UI heartbeat="+ticks);
            return 0;
        }
        finally
        {
            services.Tunnels.StopAll();
            foreach(var view in views) view.Shutdown();
            window.Close(); pulse.Stop(); Delay(200);
        }
    }
    private static void CheckDialog(SessionView view,string script,bool accept,string expected,string? input=null)
    {
        var result=Browser(view).CoreWebView2.ExecuteScriptAsync(script);
        Wait(()=>((FrameworkElement)view.FindName("ScriptDialogOverlay")).Visibility==Visibility.Visible,"ordinary script dialog");
        if(input is not null)((TextBox)view.FindName("ScriptPromptBox")).Text=input;
        ((Button)view.FindName(accept?"ScriptDialogAccept":"ScriptDialogCancel")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Wait(()=>result.IsCompleted,"script dialog response");
        Assert(result.GetAwaiter().GetResult()==expected,"script dialog returned the wrong result");
    }
    private static WebView2 Browser(SessionView view) => (WebView2)view.FindName("WebView");
    private static void Assert(bool condition,string message) { if(!condition)throw new Exception(message); }
    private static bool Listening(int port) { try { using var client=new TcpClient(); client.Connect("127.0.0.1",port); return true; } catch(SocketException){ return false; } }
    private static void Pump() { var frame=new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,()=>frame.Continue=false); Dispatcher.PushFrame(frame); Thread.Sleep(10); }
    private static void Wait(Func<bool> condition,string label,int seconds=30) { var end=DateTime.UtcNow.AddSeconds(seconds); while(!condition()) { if(DateTime.UtcNow>end)throw new Exception("Timed out: "+label); Pump(); } }
    private static void Delay(int milliseconds) { var end=DateTime.UtcNow.AddMilliseconds(milliseconds); while(DateTime.UtcNow<end)Pump(); }
}
