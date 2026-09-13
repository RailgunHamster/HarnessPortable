using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Credential vault backed by Windows DPAPI (CurrentUser scope).
///
/// Each secret is encrypted with DPAPI using per-key entropy; the resulting
/// blob is stored in <c>%APPDATA%\HarnessPortable\secrets.json</c>. Only the
/// Windows account that wrote the blob can read it back. Besides the SSH
/// password, a profile may carry an optional web login input (the dsh web
/// token URL or the token itself), keyed with a suffix so it never collides
/// with the password and never reaches <c>profiles.json</c>.
/// </summary>
public sealed class SecureStore
{
    /// <summary>Key suffix of the optional web login input of a profile.</summary>
    private const string AuthInputSuffix = "#webauth";

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

    public void SetPassword(string profileId, string password) => SetSecret(profileId, password);

    public string? GetPassword(string profileId) => GetSecret(profileId);

    public void ClearPassword(string profileId) => ClearSecret(profileId);

    /// <summary>
    /// Optional web login input of a profile: the dsh web token URL, a bare
    /// query, a key=value pair, or the token itself. Used when the profile's
    /// auth mode is "manual" and as the fallback when the NSSM fetch misses.
    /// </summary>
    public string? GetAuthInput(string profileId) => GetSecret(AuthInputKey(profileId));

    public bool HasAuthInput(string profileId) => LoadDict().ContainsKey(AuthInputKey(profileId));

    public void SetAuthInput(string profileId, string value) => SetSecret(AuthInputKey(profileId), value);

    public void ClearAuthInput(string profileId) => ClearSecret(AuthInputKey(profileId));

    private static string AuthInputKey(string profileId) => profileId + AuthInputSuffix;

    private void SetSecret(string key, string value)
    {
        var entropy = EntropyFor(key);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value),
            entropy,
            DataProtectionScope.CurrentUser);
        var dict = LoadDict();
        dict[key] = Convert.ToBase64String(protectedBytes);
        SaveDict(dict);
    }

    private string? GetSecret(string key)
    {
        var dict = LoadDict();
        if (!dict.TryGetValue(key, out var blob))
        {
            return null;
        }

        try
        {
            var raw = Convert.FromBase64String(blob);
            var entropy = EntropyFor(key);
            var plain = ProtectedData.Unprotect(raw, entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    private void ClearSecret(string key)
    {
        var dict = LoadDict();
        if (dict.Remove(key))
        {
            SaveDict(dict);
        }
    }

    private static byte[] EntropyFor(string key) =>
        Encoding.UTF8.GetBytes($"harness-portable:{key}");
}
