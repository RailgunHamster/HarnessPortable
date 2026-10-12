using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net;
using System.Net.Sockets;
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
            var config = JsonFile.Read(_path, () => new Config(), JsonOptions);
            config.Tunnels = (config.Tunnels ?? [])
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.SshHost))
                .ToList();
            config.Directs = (config.Directs ?? [])
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .ToList();
            return config;
        }
    }

    public void Save(Config config)
    {
        lock (_lock)
        {
            JsonFile.Write(_path, config, JsonOptions);
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

        if (!s.Contains("://"))
        {
            var separator = s.IndexOfAny(['/', '?', '#']);
            var authority = separator >= 0 ? s[..separator] : s;
            var remainder = separator >= 0 ? s[separator..] : "";
            if (!authority.StartsWith('[') && IPAddress.TryParse(authority, out var address) && address.AddressFamily == AddressFamily.InterNetworkV6)
                authority = $"[{authority}]:4096";
            else if (authority.StartsWith('[') && authority.EndsWith(']'))
                authority += ":4096";
            else if (!authority.Contains(':'))
                authority += ":4096";
            s = $"http://{authority}{remainder}";
        }
        return Uri.TryCreate(s, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https"
            && !string.IsNullOrWhiteSpace(uri.Host)
            && uri.Port is >= 1 and <= 65535 ? s : null;
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
