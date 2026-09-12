using System.Runtime.InteropServices;
using System.Text;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Small Win32 helpers for focus bookkeeping across the WebView2 HWND tree.
/// WPF's <c>IsKeyboardFocusWithin</c> desyncs from Win32 focus for HwndHost
/// controls (WebView2), so decisions about stealing focus must use the real
/// Win32 focus state.
/// </summary>
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int maxCount);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    private const uint GaParent = 1;

    /// <summary>
    /// True when the Win32 focused window of the foreground thread lies
    /// inside the given window tree — for a WebView2 host HWND this means
    /// the user is currently interacting with that page (caret in an input,
    /// IME composition active). Focus must never be stolen in that state:
    /// a <c>SetFocus</c> into the host cancels the in-progress composition.
    /// </summary>
    internal static bool FocusInsideWindowTree(IntPtr root)
    {
        if (root == IntPtr.Zero)
        {
            return false;
        }

        var focus = TryGetFocusWindow();
        if (focus == IntPtr.Zero)
        {
            return false;
        }

        var current = focus;
        while (current != IntPtr.Zero)
        {
            if (current == root)
            {
                return true;
            }

            current = GetAncestor(current, GaParent);
        }

        return false;
    }

    /// <summary>Human-readable Win32 focus state, for diagnostics.</summary>
    internal static string DescribeFocus()
    {
        var focus = TryGetFocusWindow();
        if (focus == IntPtr.Zero)
        {
            return "focus=none";
        }

        return "focus=" + DescribeWindow(focus);
    }

    private static IntPtr TryGetFocusWindow()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var threadId = GetWindowThreadProcessId(foreground, out _);
        if (threadId == 0)
        {
            return IntPtr.Zero;
        }

        var info = new GuiThreadInfo { cbSize = Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(threadId, ref info))
        {
            return IntPtr.Zero;
        }

        return info.hwndFocus;
    }

    private static string DescribeWindow(IntPtr window)
    {
        var name = new StringBuilder(64);
        GetClassName(window, name, 64);
        GetWindowThreadProcessId(window, out var pid);
        return window.ToInt64().ToString("X") + ":" + name + ":" + pid;
    }
}
