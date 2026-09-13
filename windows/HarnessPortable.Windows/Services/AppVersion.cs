using System.Reflection;

namespace HarnessPortable.Windows.Services;

public static class AppVersion
{
    public static string Current
    {
        get
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            var raw = string.IsNullOrWhiteSpace(informational)
                ? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"
                : informational;
            var plus = raw.IndexOf('+');
            var version = plus < 0 ? raw : raw[..plus];
            return version.Trim();
        }
    }
}
