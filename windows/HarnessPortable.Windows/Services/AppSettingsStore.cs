using System.IO;
using System.Text.Json;
using HarnessPortable.Windows.Models;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// App preferences persisted in the system per-user directory:
/// <c>%APPDATA%\HarnessPortable\settings.json</c>. Never stored next to the exe.
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _lock = new();

    public event Action? Changed;

    public AppSettingsStore(string? path = null)
    {
        _path = path ?? AppPaths.SettingsFile;
    }

    public AppSettings Load()
    {
        lock (_lock)
        {
            var settings = JsonFile.Read(_path, () => new AppSettings(), JsonOptions);
            settings.DirectAuthModes ??= [];
            settings.Connections ??= [];
            settings.ConnectionOrder ??= [];
            // 2.7.1 persisted false during layout restore without a user choice.
            settings.ReconnectRestoredWorkspace = settings.StartupReconnectPreference ?? true;
            return settings;
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_lock)
        {
            JsonFile.Write(_path, settings, JsonOptions);
        }
        Changed?.Invoke();
    }
}
