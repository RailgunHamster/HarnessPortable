using System.IO;
using System.Text.Json;
using HarnessPortable.Windows.Models;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Persists saved workspace layouts in <c>%APPDATA%\HarnessPortable\layouts.json</c>.
/// A layout records the split tree plus which tunnel profile / direct URL each
/// tab points at, and — when the <c>dsh-view-state</c> plugin is installed — the
/// tab's selected session and panel geometry, read from the plugin's three
/// whitelisted query parameters. It never stores passwords or a raw page URL
/// (dsh's startup URL carries a process token).
/// </summary>
public sealed class LayoutPresetStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _lock = new();

    public LayoutPresetStore(string? path = null)
    {
        _path = path ?? AppPaths.LayoutsFile;
    }

    public List<WorkspaceLayout> Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return [];
            }

            try
            {
                return JsonSerializer.Deserialize<List<WorkspaceLayout>>(File.ReadAllText(_path), JsonOptions)
                       ?? [];
            }
            catch
            {
                return [];
            }
        }
    }

    public void Save(IEnumerable<WorkspaceLayout> layouts)
    {
        lock (_lock)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(layouts.ToList(), JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
    }

    public void Upsert(WorkspaceLayout layout)
    {
        var all = Load();
        all.RemoveAll(l => string.Equals(l.Name, layout.Name, StringComparison.OrdinalIgnoreCase));
        all.Insert(0, layout);
        Save(all);
    }

    public void Delete(string name)
    {
        var all = Load();
        all.RemoveAll(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
        Save(all);
    }
}
