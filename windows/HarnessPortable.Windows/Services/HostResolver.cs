using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace HarnessPortable.Windows.Services;

public sealed record HostResolution(string Ip, string Source);

/// <summary>
/// Resolves SSH host names on Windows.
///
/// Unlike Android, Windows' own resolver already covers DNS, LLMNR and
/// NetBIOS name resolution, and Tailscale MagicDNS simply arrives through
/// DNS. We therefore keep one native resolution path and only special-case
/// the Tailscale CGNAT range so the UI can show a useful hint.
/// </summary>
public static class HostResolver
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);
    private static readonly object CacheLock = new();
    private static readonly Dictionary<string, (HostResolution Resolution, DateTime At)> Cache = new();

    public static bool IsIpLiteral(string host)
    {
        var h = host.Trim();
        if (h.Length == 0)
        {
            return false;
        }

        if (h.Contains(':'))
        {
            return true; // IPv6 literal
        }

        var parts = h.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        return parts.All(p => int.TryParse(p, out var v) && v is >= 0 and <= 255);
    }

    /// <summary>100.64.0.0/10 — the CGNAT range Tailscale assigns.</summary>
    public static bool IsTailscaleAddress(string ip)
    {
        var parts = ip.Split('.');
        if (parts.Length != 4)
        {
            return false;
        }

        if (!int.TryParse(parts[0], out var a) || !int.TryParse(parts[1], out var b))
        {
            return false;
        }

        return a == 100 && b is >= 64 and <= 127;
    }

    /// <summary>True when the machine currently has an active Tailscale interface.</summary>
    public static bool TailscaleActive()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(ni =>
                ni.Name.StartsWith("tailscale", StringComparison.OrdinalIgnoreCase) &&
                ni.GetIPProperties().UnicastAddresses.Any(ua =>
                    ua.Address.AddressFamily == AddressFamily.InterNetwork &&
                    IsTailscaleAddress(ua.Address.ToString())));
        }
        catch
        {
            return false;
        }
    }

    public static async Task<HostResolution?> ResolveAsync(string host, CancellationToken token = default)
    {
        var key = host.Trim().ToLowerInvariant();
        if (key.Length == 0)
        {
            return null;
        }

        if (IsIpLiteral(key))
        {
            return new HostResolution(key, "ip");
        }

        lock (CacheLock)
        {
            if (Cache.TryGetValue(key, out var cached) && DateTime.UtcNow - cached.At < Ttl)
            {
                return cached.Resolution;
            }

            Cache.Remove(key);
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(key, token).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }

        var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        if (ipv4 is null)
        {
            return null;
        }

        var source = IsTailscaleAddress(ipv4.ToString()) ? "tailscale" : "system";
        var resolution = new HostResolution(ipv4.ToString(), source);

        lock (CacheLock)
        {
            Cache[key] = (resolution, DateTime.UtcNow);
        }

        return resolution;
    }

    public static string FailureMessage(string host)
    {
        var baseMessage = $"无法解析 {host}：系统 DNS / NetBIOS 均无结果";
        return TailscaleActive()
            ? $"{baseMessage}。检测到 Tailscale：请确认 MagicDNS 已开启，或直接使用 IP 地址"
            : $"{baseMessage}。请确认与服务器在同一局域网，或使用 IP 地址";
    }
}
