using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Lightweight diagnostic logger for focus/render flicker investigations.
/// Enqueues are lock-free and never touch the disk from the caller's thread;
/// a background drainer writes with a per-tick rate cap so even a focus
/// storm cannot flood the file or slow the UI. The file self-trims to about
/// the last 2 MB. Install with <see cref="Start"/> from the UI thread — that
/// also hooks system focus events (WinEvent) so every focus change, whoever
/// causes it, is recorded with window class and process id.
/// </summary>
public static class FlickerLog
{
    private const int BudgetPerTick = 500;          // entries per drain tick
    private static readonly TimeSpan DrainInterval = TimeSpan.FromMilliseconds(200);

    private static readonly ConcurrentQueue<string> Pending = new();
    private static readonly object StartLock = new();
    private static int _started;
    private static int _budget = BudgetPerTick;
    private static int _suppressed;
    private static long _seq;
    private static System.Threading.Timer? _drainer;

    // WinEvent hook (system focus/foreground events). Kept in static fields
    // so the delegates are never collected while the hooks are alive.
    private delegate void WinEventDelegate(
        IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild,
        uint thread, uint time);

    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectFocus = 0x8005;

    private static WinEventDelegate? _focusProc;
    private static WinEventDelegate? _foregroundProc;
    private static IntPtr _focusHook;
    private static IntPtr _foregroundHook;

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr module,
        WinEventDelegate proc, uint pid, uint thread, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int max);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);

    public static void Start()
    {
        lock (StartLock)
        {
            if (Interlocked.Exchange(ref _started, 1) == 1)
            {
                return;
            }

            try
            {
                AppPaths.Ensure();
            }
            catch
            {
                // Logging is best-effort; never break startup.
            }

            try
            {
                var exe = Environment.ProcessPath;
                Log("boot", "exe=" + exe +
                            " written=" + (exe is null ? "?" : File.GetLastWriteTime(exe).ToString("O")) +
                            " user=" + Environment.UserName +
                            " interactive=" + Environment.UserInteractive);
            }
            catch
            {
                // Ignore.
            }

            _drainer = new System.Threading.Timer(_ => Drain(), null, DrainInterval, DrainInterval);

            InstallWinEventHooks();
        }
    }

    public static void Log(string source, string detail)
    {
        if (Volatile.Read(ref _started) == 0)
        {
            return;
        }

        if (Interlocked.Decrement(ref _budget) < 0)
        {
            Interlocked.Increment(ref _suppressed);
            return;
        }

        Pending.Enqueue($"{DateTime.Now:HH:mm:ss.fff} #{Interlocked.Increment(ref _seq)} [{source}] {detail}");
    }

    private static void Drain()
    {
        try
        {
            var suppressed = Interlocked.Exchange(ref _suppressed, 0);
            var builder = new StringBuilder();

            if (suppressed > 0)
            {
                builder.AppendLine($"{DateTime.Now:HH:mm:ss.fff} [rate-limit] {suppressed} entries suppressed");
            }

            while (Pending.TryDequeue(out var line))
            {
                builder.AppendLine(line);
            }

            if (builder.Length == 0)
            {
                return;
            }

            var path = Path.Combine(AppPaths.DataDirectory, "flicker.log");
            File.AppendAllText(path, builder.ToString());

            var info = new FileInfo(path);
            if (info.Exists && info.Length > 2_000_000)
            {
                using var reader = new StreamReader(path, Encoding.UTF8);
                var tail = reader.ReadToEnd();
                var cut = tail.IndexOf('\n', tail.Length / 2);
                if (cut >= 0)
                {
                    File.WriteAllText(path, tail[(cut + 1)..]);
                }
            }
        }
        catch
        {
            // Diagnostics must never break the app.
        }
        finally
        {
            Volatile.Write(ref _budget, BudgetPerTick);
        }
    }

    private static void InstallWinEventHooks()
    {
        try
        {
            // Must be installed from a thread with a message pump (the UI
            // thread) — events are delivered through its message loop.
            _focusProc = (hook, type, hwnd, idObject, idChild, thread, time) =>
            {
                if (hwnd != IntPtr.Zero)
                {
                    Log("winevent-focus", DescribeWindow(hwnd) + " tid=" + thread);
                }
            };
            _foregroundProc = (hook, type, hwnd, idObject, idChild, thread, time) =>
            {
                if (hwnd != IntPtr.Zero)
                {
                    Log("winevent-foreground", DescribeWindow(hwnd) + " tid=" + thread);
                }
            };

            _focusHook = SetWinEventHook(EventObjectFocus, EventObjectFocus, IntPtr.Zero, _focusProc, 0, 0, 0);
            _foregroundHook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _foregroundProc, 0, 0, 0);
            Log("boot", $"win-event hooks focus={_focusHook != IntPtr.Zero} foreground={_foregroundHook != IntPtr.Zero}");
        }
        catch (Exception ex)
        {
            Log("boot", "win-event hook failed: " + ex.Message);
        }
    }

    private static string DescribeWindow(IntPtr window)
    {
        try
        {
            var name = new StringBuilder(64);
            GetClassName(window, name, 64);
            uint pid;
            GetWindowThreadProcessId(window, out pid);
            return $"{window.ToInt64():X} class={name} pid={pid}";
        }
        catch
        {
            return $"{window.ToInt64():X}";
        }
    }
}
