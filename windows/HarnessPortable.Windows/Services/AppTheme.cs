using System.Windows;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Keeps the DSH token dictionary in <see cref="Application.Resources"/> in step
/// with the Windows app-mode setting (Settings → Personalization → Colors →
/// "Choose your mode"). That is the same signal the harness' own frontend follows
/// through <c>prefers-color-scheme</c>, so the management window matches both the
/// system and the harness.
///
/// The token dictionaries are DshTokens.Light.xaml / DshTokens.Dark.xaml — same
/// keys, different values — and everything in the management window references
/// them with DynamicResource, so a swap restyles the open window immediately.
/// </summary>
public static class AppTheme
{
    // Absolute pack URIs: a relative one resolves against the entry assembly,
    // which is not this one when a test host (or any other launcher) drives it.
    private const string LightTokens =
        "pack://application:,,,/HarnessPortable;component/Themes/DshTokens.Light.xaml";

    private const string DarkTokens =
        "pack://application:,,,/HarnessPortable;component/Themes/DshTokens.Dark.xaml";

    private static ResourceDictionary? _tokens;
    private static bool _started;
    private static string _appearance = "system";

    public static void SetPreference(string preference)
    {
        _appearance = preference is "light" or "dark" ? preference : "system";
        Apply(_appearance == "dark" || (_appearance == "system" && IsSystemDark()));
    }

    /// <summary>True when the dark token set is active.</summary>
    public static bool IsDark { get; private set; }
    public static event Action? Changed;

    public static void Track(Window window)
    {
        void Refresh() {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            var enabled = IsDark ? 1 : 0;
            try { DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        window.SourceInitialized += (_, _) => Refresh();
        Changed += Refresh;
        window.Closed += (_, _) => Changed -= Refresh;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        SetPreference(new AppSettingsStore().Load().Appearance);

        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch
        {
            // No interactive session / no message pump: the palette simply stays
            // whatever it was at startup.
        }
    }

    /// <summary>Swaps in the light or dark token set. Safe to call from any thread.</summary>
    public static void Apply(bool dark)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            return;
        }

        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.BeginInvoke(() => Apply(dark));
            return;
        }

        if (_tokens is not null && IsDark == dark) return;

        var next = new ResourceDictionary { Source = new Uri(dark ? DarkTokens : LightTokens, UriKind.Absolute) };
        var merged = app.Resources.MergedDictionaries;

        if (_tokens is not null)
        {
            merged.Remove(_tokens);
        }

        // First, so a view that also merges its own overrides wins.
        merged.Insert(0, next);
        _tokens = next;
        IsDark = dark;
        Changed?.Invoke();
        FlickerLog.Log("theme", dark ? "dark tokens applied" : "light tokens applied");
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle)
        {
            SetPreference(_appearance);
        }
    }

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");

            // AppsUseLightTheme == 0 means dark. Missing value: Windows default,
            // which is light.
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch
        {
            return false;
        }
    }
}
