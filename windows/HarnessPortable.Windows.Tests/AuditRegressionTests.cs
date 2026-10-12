using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Linq;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Tests;

public sealed class AuditRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hp-fix-tests");
    private readonly string _directory;

    public AuditRegressionTests()
    {
        _directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void NullDirects_PreservesValidTunnelsWhenAddingAUrl()
    {
        var path = Path.Combine(_directory, "profiles.json");
        File.WriteAllText(path, "{\"tunnels\":[{\"id\":\"one\",\"sshHost\":\"test.invalid\"}],\"directs\":null}");
        var store = new ProfileStore(path);
        store.SaveDirects(["http://localhost:3080"]);
        Assert.Equal("one", Assert.Single(store.LoadTunnels()).Id);
        Assert.True(File.Exists(path + ".bak"));
    }

    [Fact]
    public void CorruptProfiles_CannotBeOverwrittenByAnAddOrExplicitSave()
    {
        var path = Path.Combine(_directory, "profiles.json");
        const string damaged = "{truncated";
        File.WriteAllText(path, damaged);
        var store = new ProfileStore(path);
        Assert.Throws<InvalidDataException>(() => store.SaveDirects(["http://localhost"]));
        Assert.Throws<InvalidDataException>(() => store.Save(new ProfileStore.Config()));
        Assert.Equal(damaged, File.ReadAllText(path));
    }

    [Fact]
    public void CorruptTrustStore_DoesNotTrustAChangedKey()
    {
        var path = Path.Combine(_directory, "known_hosts.json");
        var store = new KnownHostsStore(path);
        Assert.Equal(HostKeyDecision.TrustedNew, store.Check("test.invalid", 22, "ssh-ed25519", [1, 2, 3]));
        File.WriteAllText(path, "{truncated");
        Assert.Throws<InvalidDataException>(() => store.Check("test.invalid", 22, "ssh-ed25519", [4, 5, 6]));
        Assert.Equal("{truncated", File.ReadAllText(path));
    }

    [Fact]
    public void TrustStoreWithNullHosts_IsRejected()
    {
        var path = Path.Combine(_directory, "known_hosts.json");
        File.WriteAllText(path, "{\"Hosts\":null}");
        Assert.Throws<InvalidDataException>(() => new KnownHostsStore(path).Check("test.invalid", 22, "ssh-ed25519", [1]));
    }

    [Fact]
    public void LocalAuthentication_NeverGraftsTokensToOtherHosts()
    {
        Assert.Null(LocalNssmAuth.GraftToken("http://unrelated.invalid:3080/", "http://127.0.0.1:3080/?token=synthetic"));
        Assert.Null(LocalNssmAuth.GraftToken("http://localhost:3080/", "http://unrelated.invalid:3080/?token=synthetic"));
        Assert.Contains("token=synthetic", LocalNssmAuth.GraftToken("http://[::1]:3080/", "http://localhost:3080/?token=synthetic"));
    }

    [Theory]
    [InlineData("http://")]
    [InlineData("https://a b")]
    [InlineData("file:///C:/test.txt")]
    [InlineData("javascript://alert(1)")]
    [InlineData("http://localhost:99999/")]
    public void InvalidDirectUrls_AreRejected(string input) => Assert.Null(ProfileStore.NormalizeUrl(input));

    [Theory]
    [InlineData("::1", "http://[::1]:4096")]
    [InlineData("[::1]", "http://[::1]:4096")]
    [InlineData("[::1]:3080", "http://[::1]:3080")]
    [InlineData("host/path", "http://host:4096/path")]
    [InlineData("https://example.test/path", "https://example.test/path")]
    public void DirectUrls_PreserveOriginsAndHandleIPv6(string input, string expected) => Assert.Equal(expected, ProfileStore.NormalizeUrl(input));

    [Fact]
    public void DiagnosticUrls_RemoveTokensUserInfoAndFragments()
    {
        var redacted = DiagnosticUrls.Redact("https://user:password@example.test/path?token=synthetic#secret");
        Assert.Equal("https://example.test/path?…", redacted);
        Assert.DoesNotContain("synthetic", redacted);
        Assert.DoesNotContain("password", redacted);
        Assert.DoesNotContain("secret", redacted);
    }

    [Fact]
    public async Task DisposalWatchdog_RecoversAnAlreadyExecutingCall()
    {
        using var blocked = new ManualResetEventSlim();
        var recovered = 0;
        var operation = Task.Run(() => GuardedDisposal.Run(
            () => Assert.True(blocked.Wait(TimeSpan.FromSeconds(5))),
            () => { Interlocked.Increment(ref recovered); blocked.Set(); }, TimeSpan.FromMilliseconds(50)));
        await operation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, recovered);
    }

    [Fact]
    public async Task DisposalWatchdog_DoesNotRecoverSuccessfulDisposal()
    {
        var recovered = 0;
        GuardedDisposal.Run(() => { }, () => Interlocked.Increment(ref recovered), TimeSpan.FromMilliseconds(30));
        await Task.Delay(100);
        Assert.Equal(0, recovered);
    }

    [Theory]
    [InlineData(780, 70, 120, 500)]
    [InlineData(0, 540, 900, 60)]
    [InlineData(860, 560, 40, 40)]
    public void DragCard_RemainsInsideTheViewport(double x, double y, double width, double height)
    {
        var card = TabDropGeometry.CardBounds(new Rect(x, y, width, height), new Size(900, 600), new Size(238, 174));
        Assert.True(new Rect(0, 0, 900, 600).Contains(card));
        Assert.Equal(238, card.Width);
        Assert.Equal(174, card.Height);
    }

    [Fact]
    public void DragHitTest_UsesTheRealXamlButtonEdges()
    {
        RunSta(() =>
        {
            XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
            var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainWindow.xaml"));
            var canvasMarkup = new XElement(document.Descendants(ns + "Canvas").Single(element => (string?)element.Attribute(x + "Name") == "TabDragOverlay"));
            var markup = $"<Grid xmlns='{ns}' xmlns:x='{x}'><Grid.Resources><ResourceDictionary Source='pack://application:,,,/HarnessPortable;component/Themes/Modern.xaml'/></Grid.Resources>{canvasMarkup}</Grid>";
            var root = (Grid)XamlReader.Parse(markup);
            var canvas = (Canvas)root.FindName("TabDragOverlay");
            canvas.Visibility = Visibility.Visible;
            var card = (Border)root.FindName("DropZoneCard");
            card.Visibility = Visibility.Visible;
            Canvas.SetLeft(card, 100);
            Canvas.SetTop(card, 100);
            root.Measure(new Size(900, 600));
            root.Arrange(new Rect(0, 0, 900, 600));
            root.UpdateLayout();
            var names = new Dictionary<TabDropZone, string>
            {
                [TabDropZone.Up] = "DropUpButton", [TabDropZone.Down] = "DropDownButton",
                [TabDropZone.Left] = "DropLeftButton", [TabDropZone.Right] = "DropRightButton", [TabDropZone.Center] = "DropCenterButton",
            };
            var bounds = names.ToDictionary(pair => pair.Key, pair =>
            {
                var button = (System.Windows.Controls.Button)root.FindName(pair.Value);
                return button.TransformToAncestor(canvas).TransformBounds(new Rect(button.RenderSize));
            });
            foreach (var target in bounds)
            {
                var r = target.Value;
                foreach (var point in new[] { new Point(r.Left + r.Width / 2, r.Top + 1), new Point(r.Left + r.Width / 2, r.Bottom - 1) })
                    Assert.Equal(target.Key, TabDropGeometry.ZoneAt(new Rect(0, 0, 900, 600), point, bounds));
            }
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "WPF layout verification timed out");
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    public void Dispose()
    {
        var resolved = Path.GetFullPath(_directory);
        if (resolved.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            Directory.Delete(resolved, recursive: true);
    }
}
