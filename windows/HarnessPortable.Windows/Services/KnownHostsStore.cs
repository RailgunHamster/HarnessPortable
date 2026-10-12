using System.IO;
using System.Text.Json;

namespace HarnessPortable.Windows.Services;

public enum HostKeyDecision
{
    /// <summary>No key was on file for this host; the presented key was recorded.</summary>
    TrustedNew,

    /// <summary>The presented key matches the key on file.</summary>
    TrustedMatch,

    /// <summary>The presented key differs from the key on file. The caller must abort.</summary>
    Changed,
}

/// <summary>
/// Trust-on-first-use host key store persisted in <c>known_hosts.json</c>.
/// The first connection to a host remembers its raw key; a changed key is
/// reported as <see cref="HostKeyDecision.Changed"/> so the caller can refuse
/// the connection (MITM protection).
/// </summary>
public sealed class KnownHostsStore
{
    private sealed class FileFormat
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, Entry> Hosts { get; set; } = [];
    }

    private sealed class Entry
    {
        public string Type { get; set; } = "";
        public string KeyBase64 { get; set; } = "";
    }

    private readonly string _path;
    private readonly object _lock = new();

    public KnownHostsStore(string? path = null)
    {
        _path = path ?? AppPaths.KnownHostsFile;
    }

    public static string HostKeyId(string host, int port) => $"{host}:{port}";

    public HostKeyDecision Check(string host, int port, string keyType, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(key);

        var id = HostKeyId(host, port);
        var presented = Convert.ToBase64String(key);

        lock (_lock)
        {
            var file = LoadFile();
            if (file.Hosts.TryGetValue(id, out var stored))
            {
                if (!string.Equals(stored.Type, keyType, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(stored.KeyBase64, presented, StringComparison.Ordinal))
                {
                    return HostKeyDecision.Changed;
                }

                return HostKeyDecision.TrustedMatch;
            }

            file.Hosts[id] = new Entry { Type = keyType, KeyBase64 = presented };
            SaveFile(file);
            return HostKeyDecision.TrustedNew;
        }
    }

    public void Remove(string host, int port)
    {
        lock (_lock)
        {
            var file = LoadFile();
            if (file.Hosts.Remove(HostKeyId(host, port)))
            {
                SaveFile(file);
            }
        }
    }

    private FileFormat LoadFile()
    {
        var file = JsonFile.Read(_path, () => new FileFormat());
        if (file.Hosts is null || file.Hosts.Values.Any(entry => entry is null || string.IsNullOrWhiteSpace(entry.Type) || string.IsNullOrWhiteSpace(entry.KeyBase64)))
            throw new InvalidDataException($"主机密钥记录已损坏，拒绝自动信任：{_path}");
        return file;
    }

    private void SaveFile(FileFormat file)
    {
        JsonFile.Write(_path, file);
    }
}
