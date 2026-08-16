using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Password vault backed by Windows DPAPI (CurrentUser scope).
///
/// Each password is encrypted with DPAPI using per-profile entropy; the
/// resulting blob is stored in <c>%APPDATA%\HarnessPortable\secrets.json</c>.
/// Only the Windows account that wrote the blob can read it back.
/// </summary>
public sealed class SecureStore
{
    private readonly string _path;
    private readonly object _lock = new();

    public SecureStore(string? path = null)
    {
        _path = path ?? AppPaths.SecretsFile;
    }

    private Dictionary<string, string> LoadDict()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return new Dictionary<string, string>();
            }

            try
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
            catch
            {
                return new Dictionary<string, string>();
            }
        }
    }

    private void SaveDict(Dictionary<string, string> dict)
    {
        lock (_lock)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(dict));
            File.Move(tmp, _path, overwrite: true);
        }
    }

    public bool HasPassword(string profileId) => LoadDict().ContainsKey(profileId);

    public void SetPassword(string profileId, string password)
    {
        var entropy = EntropyFor(profileId);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(password),
            entropy,
            DataProtectionScope.CurrentUser);
        var dict = LoadDict();
        dict[profileId] = Convert.ToBase64String(protectedBytes);
        SaveDict(dict);
    }

    public string? GetPassword(string profileId)
    {
        var dict = LoadDict();
        if (!dict.TryGetValue(profileId, out var blob))
        {
            return null;
        }

        try
        {
            var raw = Convert.FromBase64String(blob);
            var entropy = EntropyFor(profileId);
            var plain = ProtectedData.Unprotect(raw, entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    public void ClearPassword(string profileId)
    {
        var dict = LoadDict();
        if (dict.Remove(profileId))
        {
            SaveDict(dict);
        }
    }

    private static byte[] EntropyFor(string profileId) =>
        Encoding.UTF8.GetBytes($"harness-portable:{profileId}");
}
