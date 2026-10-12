using System.IO;
using System.Text.Json;
using HarnessPortable.Windows.Models;

namespace HarnessPortable.Windows.Services;

public sealed class WorkspaceSnapshotStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(AppPaths.DataDirectory, "workspace.json");
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public WorkspaceLayout? Load() => JsonFile.Read<WorkspaceLayout?>(_path, () => null, Options);
    public void Save(WorkspaceLayout layout) => JsonFile.Write(_path, layout, Options);
}
