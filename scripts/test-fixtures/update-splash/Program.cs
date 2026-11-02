using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using HarnessPortable.Windows.Services;

var output = Path.GetFullPath(args.FirstOrDefault() ?? throw new ArgumentException("Supply a new output directory."));
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Use an empty fixture directory.");
Directory.CreateDirectory(output);
var owned = new List<Process>();
var checks = new List<string>();
void Check(bool success, string label) { if (!success) throw new Exception(label); checks.Add(label); Console.WriteLine("PASS " + label); }
async Task Wait(Func<bool> predicate, string label, int milliseconds = 10000)
{
    var timer = Stopwatch.StartNew();
    while (!predicate()) { if (timer.ElapsedMilliseconds > milliseconds) throw new TimeoutException(label); await Task.Delay(25); }
}
Process Start(string executable, params string[] arguments)
{
    var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in arguments) start.ArgumentList.Add(argument);
    var process = Process.Start(start)!; owned.Add(process); return process;
}
Process Splash(string executable, string session, string root, int oldPid, params string[] extra)
{
    Directory.CreateDirectory(session);
    var start = UpdateTransitionSplash.CreateStartInfo(executable, session, root, "2.8.1", "2.8.2", false, oldPid);
    start.ArgumentList.Add("--offscreen");
    foreach (var argument in extra) start.ArgumentList.Add(argument);
    var process = Process.Start(start)!; owned.Add(process); return process;
}
try
{
    var helper = await UpdateTransitionSplash.EnsureBinaryAsync(Path.Combine(output, "helper cache"));
    var logo = Path.Combine(output, "logo.png");
    using (var resource = typeof(UpdateService).Assembly.GetManifestResourceStream("UpdateSplash.Logo")!)
    using (var file = File.Create(logo)) resource.CopyTo(file);
    Check(File.Exists(helper), "helper compiles from embedded resources without loose application source files");
    Check(!Directory.GetFiles(Path.GetDirectoryName(helper)!, "*.cs").Any(), "compiler source files are cleaned after compilation");
    var root = Path.Combine(output, "fixture install");
    var foreignRoot = root + "-other";
    Directory.CreateDirectory(Path.Combine(root, "current")); Directory.CreateDirectory(Path.Combine(foreignRoot, "current"));
    var fixtureSource = Path.Combine(output, "Fixture.cs");
    await File.WriteAllTextAsync(fixtureSource, """
        using System; using System.IO; using System.Threading; using System.Windows.Forms;
        sealed class Window : Form {
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams { get { var p=base.CreateParams; p.ExStyle|=0x08000080; return p; } }
        }
        static class Fixture {
            [STAThread] static void Main(string[] args) {
                while (!File.Exists(args[1])) Thread.Sleep(25);
                if (args[0] == "wait") return;
                Application.Run(new Window { Text="Isolated update fixture", Width=300, Height=200,
                    StartPosition=FormStartPosition.Manual, Left=-30000, Top=0 });
            }
        }
        """);
    var main = Path.Combine(root, "current", "HarnessPortable.exe");
    var compiler = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET/Framework64/v4.0.30319/csc.exe");
    var compile = Start(compiler, "/nologo", "/target:winexe", "/r:System.Windows.Forms.dll", "/out:" + main, fixtureSource);
    await compile.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Check(compile.ExitCode == 0, "compile isolated dummy old/updater/new processes");
    var oldExe = Path.Combine(output, "OldHarness.exe"); File.Copy(main, oldExe);
    var updateExe = Path.Combine(root, "Update.exe"); File.Copy(main, updateExe);
    var foreignExe = Path.Combine(foreignRoot, "current", "HarnessPortable.exe"); File.Copy(main, foreignExe);
    var oldGate = Path.Combine(output, "old-exit"); var updateGate = Path.Combine(output, "updater-exit");
    var newGate = Path.Combine(output, "new-window"); var foreignGate = Path.Combine(output, "foreign-window");
    var old = Start(oldExe, "wait", oldGate); var updater = Start(updateExe, "wait", updateGate);
    var session = Path.Combine(output, "handoff session"); var splash = Splash(helper, session, root, old.Id);
    await Wait(() => File.Exists(Path.Combine(session, "ready")), "splash render handshake");
    var handle = new IntPtr(long.Parse(await File.ReadAllTextAsync(Path.Combine(session, "ready"))));
    Check((Native.GetWindowLong(handle, -20) & 0x08000000) != 0, "transition window does not activate or steal keyboard focus");
    Check(File.Exists(Path.Combine(session, "observed-closing")) && !splash.HasExited, "splash remains while old process shuts down");
    Directory.CreateDirectory(Path.Combine(root, "packages", "VelopackTemp", "new"));
    File.WriteAllText(oldGate, ""); await old.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await Wait(() => File.Exists(Path.Combine(session, "observed-extracting")), "extracting");
    File.WriteAllText(foreignGate, ""); var foreign = Start(foreignExe, "window", foreignGate);
    await Wait(() => foreign.MainWindowHandle != IntPtr.Zero, "foreign fixture window");
    await Task.Delay(300); Check(!splash.HasExited, "a same-name window in a sibling installation cannot dismiss the splash");
    Directory.CreateDirectory(Path.Combine(root, "packages", "VelopackTemp", "backup"));
    await Wait(() => File.Exists(Path.Combine(session, "observed-replacing")), "replacing");
    var next = Start(main, "window", newGate);
    File.WriteAllText(updateGate, ""); await updater.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    await Wait(() => File.Exists(Path.Combine(session, "observed-starting")), "starting");
    await Task.Delay(5500); Check(!splash.HasExited, "slow replacement startup keeps the transition visible until its window is ready");
    File.WriteAllText(newGate, ""); await splash.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    Check(splash.ExitCode == 0 && File.Exists(Path.Combine(session, "observed-handoff")), "new matching window completes the handoff and closes the helper");
    foreach (var theme in new[] { "Light", "Dark" })
    {
        var image = Path.Combine(output, theme.ToLowerInvariant() + ".png");
        var preview = Start(helper, "--offscreen", "--theme", theme, "--from-version", "2.8.1", "--to-version", "2.8.2",
            "--logo", logo, "--screenshot", image, "--demo-stage", theme == "Light" ? "1" : "2");
        await preview.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Check(preview.ExitCode == 0 && new FileInfo(image).Length > 1000, theme + " theme renders a standalone verification image");
    }
    var blocker = Start(oldExe, "wait", Path.Combine(output, "never-release"));
    var cancelled = Path.Combine(output, "cancel session"); var cancelProcess = Splash(helper, cancelled, root, blocker.Id);
    await Wait(() => File.Exists(Path.Combine(cancelled, "ready")), "cancel fixture ready"); File.WriteAllText(Path.Combine(cancelled, "cancel"), "");
    await cancelProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Check(File.Exists(Path.Combine(cancelled, "observed-cancelled")) && !blocker.HasExited, "aborted installation closes only its helper");
    var timeout = Path.Combine(output, "timeout session"); var timeoutProcess = Splash(helper, timeout, root, blocker.Id, "--timeout-ms", "400");
    await timeoutProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Check(File.Exists(Path.Combine(timeout, "observed-timeout")), "watchdog closes a stalled transition");
    await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new { checks, passed = checks.Count }, new JsonSerializerOptions { WriteIndented = true }));
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
finally
{
    foreach (var process in owned.AsEnumerable().Reverse())
    { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } process.Dispose(); }
}
return 0;
internal static class Native
{
    [DllImport("user32.dll", EntryPoint="GetWindowLongW")] internal static extern int GetWindowLong(IntPtr window, int index);
}
