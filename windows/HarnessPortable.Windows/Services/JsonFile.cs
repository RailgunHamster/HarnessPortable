using System.IO;
using System.Text.Json;

namespace HarnessPortable.Windows.Services;

internal static class JsonFile
{
    public static T Read<T>(string path, Func<T> missing, JsonSerializerOptions? options = null)
    {
        if (!File.Exists(path)) return missing();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options)
                ?? throw new JsonException("配置内容不能为 null");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"配置文件已损坏，原文件未被覆盖：{path}。请从备份 {path}.bak 恢复。", ex);
        }
    }

    public static void Write<T>(string path, T value, JsonSerializerOptions? options = null)
    {
        // An existing unreadable file is never silently replaced by defaults.
        if (File.Exists(path))
        {
            _ = Read<T>(path, () => value, options);
            File.Copy(path, path + ".bak", overwrite: true);
        }
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, options));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
