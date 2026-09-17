using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Lightweight diagnostic logger for focus/render flicker investigations.
/// Enqueues are lock-free and never touch the disk from the caller's thread;
/// a background drainer writes with a per-tick rate cap so even a focus
/// storm cannot flood the file or slow the UI.
///
/// The file stays strictly bounded by rotation: the live file is
/// <c>flicker.log</c> and the previous one is <c>flicker.log.1</c>, each
/// capped at <see cref="MaxFileBytes"/>. Reaching a cap never reads the file
/// back in, so a write costs O(1) no matter how large the log ever got. The
/// previous tail-trim implementation rewrote the whole file from memory,
/// failed silently once the file outgrew its budget, and was observed at
/// 146 MB with a full read+rewrite retry every 200 ms — a large file on a
/// slow or scanned disk is more than enough to make the window look wedged.
///
/// Install with <see cref="Start"/> from the UI thread — that also hooks
/// system focus events (WinEvent) so every focus change, whoever causes it,
/// is recorded with window class and process id.
/// </summary>
public static class FlickerLog
{
    /// <summary>Entries accepted per drain tick. Everything above that is counted, not queued.</summary>
    private const int BudgetPerTick = 400;

    /// <summary>Entries drained per tick, so a backlog cannot grow a batch without bound.</summary>
    private const int MaxEntriesPerDrain = 2000;

    /// <summary>Cap for each generation of the log; at most two generations exist.</summary>
    internal const long MaxFileBytes = 2 * 1024 * 1024;

    /// <summary>A legacy file larger than this is rewritten once, keeping only its tail.</summary>
    private const long OversizeRewriteThreshold = 8 * 1024 * 1024;

    private const long OversizeKeepBytes = 2 * 1024 * 1024;

    private static readonly ConcurrentQueue<Entry> Pending = new();
    private static readonly object StartLock = new();
    private static int _started;
    private static int _budget = BudgetPerTick;
    private static int _suppressed;
    private static long _seq;
    private static TimeSpan _drainInterval = TimeSpan.FromMilliseconds(200);
    private static string _logDirectory = AppPaths.DataDirectory;
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

    /// <summary>One queued log line. Kept structured so folding never re-parses text.</summary>
    internal readonly record struct Entry(string Stamp, long Sequence, string Source, string Detail);

    /// <summary>Rebuilds entries from rendered lines, so folding can be tested as text.</summary>
    internal static IReadOnlyList<Entry> EntriesFromLines(IEnumerable<string> lines)
    {
        var entries = new List<Entry>();
        foreach (var line in lines)
        {
            var stamp = line.Length >= 12 ? line[..12] : line;
            var source = string.Empty;
            var detail = string.Empty;
            long sequence = 0;

            var open = line.IndexOf('[');
            var close = open >= 0 ? line.IndexOf(']', open) : -1;
            if (close > open)
            {
                source = line[(open + 1)..close];
                detail = close + 2 <= line.Length ? line[(close + 2)..] : string.Empty;

                var hash = line.IndexOf('#', 12);
                if (hash >= 0)
                {
                    var end = line.IndexOf(' ', hash);
                    if (end > hash + 1 &&
                        long.TryParse(line.AsSpan(hash + 1, end - hash - 1), out var parsed))
                    {
                        sequence = parsed;
                    }
                }
            }

            entries.Add(new Entry(stamp, sequence, source, detail));
        }

        return entries;
    }

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
                Directory.CreateDirectory(_logDirectory);
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

            _drainer = new System.Threading.Timer(_ => Drain(), null, _drainInterval, _drainInterval);

            InstallWinEventHooks();
        }
    }

    public static void Log(string source, string detail)
    {
        if (Volatile.Read(ref _started) == 0)
        {
            return;
        }

        Enqueue(source, detail);
    }

    private static void Enqueue(string source, string detail)
    {
        if (Interlocked.Decrement(ref _budget) < 0)
        {
            Interlocked.Increment(ref _suppressed);
            return;
        }

        Pending.Enqueue(new Entry(
            DateTime.Now.ToString("HH:mm:ss.fff"),
            Interlocked.Increment(ref _seq),
            source,
            detail));
    }

    // ------------------------------------------------------------------
    // Test seams. The logger is static and process-wide by design; these
    // let a test drive one drain against a private directory without
    // starting the timer or installing WinEvent hooks.
    // ------------------------------------------------------------------

    internal static string LogPath => Path.Combine(_logDirectory, "flicker.log");

    internal static string BackupLogPath => LogPath + ".1";

    internal static void ConfigureForTests(string directory, TimeSpan drainInterval)
    {
        _logDirectory = directory;
        _drainInterval = drainInterval;
        Directory.CreateDirectory(directory);
    }

    internal static void ResetForTests()
    {
        while (Pending.TryDequeue(out _))
        {
            // Discard leftovers.
        }

        Interlocked.Exchange(ref _suppressed, 0);
        Volatile.Write(ref _budget, BudgetPerTick);
    }

    internal static void DrainOnceForTests() => Drain();

    /// <summary>Queues a line without the <see cref="Start"/> gate tests never set.</summary>
    internal static void EnqueueForTests(string source, string detail) => Enqueue(source, detail);

    /// <summary>
    /// Appends one batch and keeps the log bounded. Exposed for tests, which
    /// rely on it being the only place that touches the file.
    /// </summary>
    internal static void AppendBounded(string text)
    {
        var path = LogPath;
        RotateIfNeeded(path);

        File.AppendAllText(path, text);

        var info = new FileInfo(path);
        if (info.Exists && info.Length > MaxFileBytes)
        {
            // The live file just crossed the cap: park it and start fresh.
            // The batch stays on disk in flicker.log.1 instead of being lost.
            Rotate(path);
        }
    }

    /// <summary>
    /// Formats one drain batch: consecutive identical events fold into a
    /// single line carrying a repeat count, so a focus storm costs a handful
    /// of lines instead of hundreds of thousands. Exposed for tests.
    /// </summary>
    internal static string FormatBatch(IReadOnlyList<Entry> entries, int suppressed)
    {
        var builder = new StringBuilder();

        if (suppressed > 0)
        {
            builder.AppendLine($"{DateTime.Now:HH:mm:ss.fff} [rate-limit] {suppressed} entries suppressed");
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var current = entries[i];
            var folded = 1;

            // Only the event itself decides a fold: a storm repeats the same
            // source+detail with a fresh sequence number each time.
            while (i + folded < entries.Count && SameEvent(entries[i + folded], current))
            {
                folded++;
            }

            builder.AppendLine(Line(current) + (folded > 1 ? " (x" + folded + ")" : ""));
            i += folded - 1;
        }

        return builder.ToString();
    }

    private static bool SameEvent(Entry left, Entry right) =>
        string.Equals(left.Source, right.Source, StringComparison.Ordinal) &&
        string.Equals(left.Detail, right.Detail, StringComparison.Ordinal);

    private static string Line(Entry entry) =>
        $"{entry.Stamp} #{entry.Sequence} [{entry.Source}] {entry.Detail}";

    private static void Drain()
    {
        try
        {
            var suppressed = Interlocked.Exchange(ref _suppressed, 0);

            var batch = new List<Entry>(BudgetPerTick);
            while (batch.Count < MaxEntriesPerDrain && Pending.TryDequeue(out var entry))
            {
                batch.Add(entry);
            }

            if (batch.Count == 0 && suppressed == 0)
            {
                return;
            }

            AppendBounded(FormatBatch(batch, suppressed));
        }
        catch
        {
            // Diagnostics must never break the app; the next tick retries.
        }
        finally
        {
            Volatile.Write(ref _budget, BudgetPerTick);
        }
    }

    private static void RotateIfNeeded(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            return;
        }

        if (info.Length > OversizeRewriteThreshold)
        {
            // A file this large can only come from the old implementation,
            // which grew a single file forever. Stream its newest chunk into
            // the backup and start a fresh live file, so the cost is one
            // sequential pass without loading anything into memory.
            RotateLegacy(path);
            return;
        }

        if (info.Length >= MaxFileBytes * 2)
        {
            Rotate(path);
        }
    }

    private static void Rotate(string path)
    {
        try
        {
            var backup = BackupLogPath;
            File.Delete(backup);
            File.Move(path, backup);
        }
        catch
        {
            // Rotation is best-effort; the append that follows still succeeds.
        }
    }

    private static void RotateLegacy(string path)
    {
        try
        {
            RewriteTail(path, BackupLogPath, OversizeKeepBytes);
        }
        catch
        {
            // Leaving the oversized file in place is the safe failure: the
            // next append still lands and the next tick retries the rewrite.
        }
    }

    /// <summary>
    /// Copy the newest <paramref name="keepBytes"/> of <paramref name="source"/>
    /// into <paramref name="destination"/>, then drop the oversized original.
    /// Everything is staged through a temp file, and the original is only
    /// deleted after the staged copy is in place, so a failure leaves the
    /// original exactly as it was and logging never depends on this working.
    /// </summary>
    internal static void RewriteTail(string source, string destination, long keepBytes)
    {
        var staged = destination + ".trim";
        TryDelete(staged);

        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var output = new FileStream(staged, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var skip = input.Length - keepBytes;
                if (skip > 0)
                {
                    input.Seek(skip, SeekOrigin.Begin);

                    // Drop the partial line the seek landed inside.
                    var probe = new byte[1];
                    while (input.Read(probe, 0, 1) == 1 && probe[0] != (byte)'\n')
                    {
                        // Skip ahead.
                    }
                }

                input.CopyTo(output);
            }

            File.Move(staged, destination, overwrite: true);
        }
        catch
        {
            TryDelete(staged);
            throw;
        }

        TryDelete(source);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Ignore.
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
