using System.Windows;
using Microsoft.Win32;

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

    /// <summary>True when the dark token set is active.</summary>
    public static bool IsDark { get; private set; }

    public static void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        Apply(IsSystemDark());

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
        FlickerLog.Log("theme", dark ? "dark tokens applied" : "light tokens applied");
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General
            or UserPreferenceCategory.Color
            or UserPreferenceCategory.VisualStyle)
        {
            Apply(IsSystemDark());
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
