using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HarnessPortable.Windows.Models;

namespace HarnessPortable.Windows.Services;

public sealed class ProfileStore
{
    public sealed class Config
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("tunnels")]
        public List<TunnelProfile> Tunnels { get; set; } = [];

        [JsonPropertyName("directs")]
        public List<string> Directs { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _path;
    private readonly object _lock = new();

    public ProfileStore(string? path = null)
    {
        _path = path ?? AppPaths.ProfilesFile;
    }

    public Config Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_path))
            {
                return new Config();
            }

            try
            {
                var json = File.ReadAllText(_path);
                var config = JsonSerializer.Deserialize<Config>(json, JsonOptions) ?? new Config();
                config.Tunnels = config.Tunnels
                    .Where(p => p != null && !string.IsNullOrWhiteSpace(p.SshHost))
                    .ToList();
                config.Directs = config.Directs
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .ToList();
                return config;
            }
            catch
            {
                return new Config();
            }
        }
    }

    public void Save(Config config)
    {
        lock (_lock)
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
    }

    public List<TunnelProfile> LoadTunnels() => Load().Tunnels;

    public void SaveTunnels(IEnumerable<TunnelProfile> profiles)
    {
        var config = Load();
        config.Tunnels = profiles.ToList();
        Save(config);
    }

    public List<string> LoadDirects() => Load().Directs;

    public void SaveDirects(IEnumerable<string> directs)
    {
        var config = Load();
        config.Directs = directs.ToList();
        Save(config);
    }

    public TunnelProfile? FindTunnel(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return LoadTunnels().FirstOrDefault(p => p.Id == id);
    }

    /// <summary>
    /// Normalizes what the user typed in the "direct URL" box.
    /// 192.168.0.104        → http://192.168.0.104:4096
    /// host:3080            → http://host:3080
    /// http(s)://...        → unchanged
    /// </summary>
    public static string? NormalizeUrl(string? raw)
    {
        var s = raw?.Trim();
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        if (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("://"))
        {
            return s;
        }

        var withPort = s.Contains(':') ? s : $"{s}:4096";
        return $"http://{withPort}";
    }

    public static string HostOf(string url)
    {
        try
        {
            var uri = new Uri(url);
            return string.IsNullOrEmpty(uri.Host) ? url : uri.Host;
        }
        catch
        {
            return url;
        }
    }
}
