using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace HarnessPortable.Windows.Services;

public sealed record HostResolution(string Ip, string Source);
public sealed record LanMachine(string Name, string Ip);

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

    private static readonly TimeSpan DiscoveryTtl = TimeSpan.FromSeconds(30);
    private static readonly object DiscoveryLock = new();
    private static Task<IReadOnlyList<LanMachine>>? _discoveryTask;
    private static IReadOnlyList<LanMachine> _discoveredMachines = [];
    private static DateTime _discoveredAt;

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
            // Bound the resolution: after sleep/resume or a network switch
            // the OS resolver can wedge and stall callers for minutes.
            using var dnsTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            dnsTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            addresses = await Dns.GetHostAddressesAsync(key, dnsTimeout.Token).ConfigureAwait(false);
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

    /// <summary>
    /// Resolves the host name inside an http(s) URL to an IPv4 literal, e.g.
    /// http://winserver:4096 → http://192.168.0.104:4096. Returns null when
    /// the URL has no host name to resolve (IP literal, non-http scheme,
    /// unparseable) or the name is unknown — callers then fall back to the
    /// browser's own resolution, i.e. the original URL.
    /// </summary>
    public static async Task<string?> ResolveUrlAsync(string url, CancellationToken token = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            {
                return null;
            }

            if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var host = uri.Host;
            if (host.Length == 0 || IsIpLiteral(host))
            {
                return null;
            }

            var resolution = await ResolveAsync(host, token).ConfigureAwait(false);
            return resolution is null ? null : ReplaceHost(url, resolution.Ip);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Replaces the host inside an http(s) URL with <paramref name="ip"/>,
    /// keeping scheme, userinfo, port, path, query and fragment untouched.
    /// Returns null when the URL cannot be parsed or its host cannot be
    /// located verbatim in the authority (e.g. bracketed IPv6).
    /// </summary>
    public static string? ReplaceHost(string url, string ip)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(ip))
        {
            return null;
        }

        var s = url.Trim();
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (uri.Host.Length == 0)
        {
            return null;
        }

        var authorityStart = s.IndexOf("://", StringComparison.Ordinal) + 3;
        if (authorityStart < 3)
        {
            return null;
        }

        // The authority runs to the first path/query/fragment character.
        var authorityEnd = authorityStart;
        while (authorityEnd < s.Length && s[authorityEnd] is not ('/' or '?' or '#'))
        {
            authorityEnd++;
        }

        // Skip userinfo (may itself contain ':'); the host starts after the last '@'.
        var hostStart = authorityStart;
        for (var i = authorityStart; i < authorityEnd; i++)
        {
            if (s[i] == '@')
            {
                hostStart = i + 1;
            }
        }

        // The host ends at the port separator, if any.
        var hostEnd = authorityEnd;
        for (var i = hostStart; i < authorityEnd; i++)
        {
            if (s[i] == ':')
            {
                hostEnd = i;
                break;
            }
        }

        var rawHost = s.Substring(hostStart, hostEnd - hostStart);
        if (!string.Equals(rawHost, uri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null; // percent-encoded or otherwise unusual — don't guess
        }

        var replacement = ip.Contains(':') ? $"[{ip}]" : ip; // IPv6 needs brackets
        return string.Concat(s.AsSpan(0, hostStart), replacement, s.AsSpan(hostEnd));
    }

    public static void ResetLanMachineDiscovery()
    {
        lock (DiscoveryLock)
        {
            _discoveredAt = default;
            if (_discoveryTask?.IsCompleted == true)
            {
                _discoveryTask = null;
            }
        }
    }

    /// <summary>
    /// Finds machine names advertised on the current LAN. Discovery is
    /// intentionally bounded and cached because it is only a suggestion aid;
    /// manual host input remains valid when a network does not answer.
    /// </summary>
    public static async Task<IReadOnlyList<LanMachine>> DiscoverLanMachinesAsync(
        CancellationToken token = default)
    {
        Task<IReadOnlyList<LanMachine>> task;
        lock (DiscoveryLock)
        {
            if (DateTime.UtcNow - _discoveredAt < DiscoveryTtl)
            {
                return _discoveredMachines;
            }

            if (_discoveryTask is null || _discoveryTask.IsCompleted)
            {
                _discoveryTask = DiscoverLanMachinesCoreAsync();
            }

            task = _discoveryTask;
        }

        var result = await task.WaitAsync(token).ConfigureAwait(false);
        lock (DiscoveryLock)
        {
            _discoveredMachines = result;
            _discoveredAt = DateTime.UtcNow;
        }

        return result;
    }

    private static async Task<IReadOnlyList<LanMachine>> DiscoverLanMachinesCoreAsync()
    {
        try
        {
            var machines = await DiscoverNodeStatusAsync().ConfigureAwait(false);
            return MergeMachines(machines, []);
        }
        catch
        {
            return [];
        }
    }

    private static async Task<IReadOnlyList<LanMachine>> DiscoverNodeStatusAsync()
    {
        var broadcasts = GetBroadcastAddresses();
        if (broadcasts.Count == 0)
        {
            return [];
        }

        var transactionId = (ushort)Random.Shared.Next(0, 0x10000);
        var query = BuildNodeStatusQuery(transactionId);
        var machines = new List<LanMachine>();

        using var socket = new UdpClient(AddressFamily.InterNetwork)
        {
            EnableBroadcast = true,
        };

        foreach (var broadcast in broadcasts)
        {
            try
            {
                await socket.SendAsync(query, query.Length, new IPEndPoint(broadcast, 137))
                    .ConfigureAwait(false);
            }
            catch
            {
                // Another interface or a restricted network may reject broadcast.
            }
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var packet = await socket.ReceiveAsync(timeout.Token).ConfigureAwait(false);
                foreach (var name in ParseNodeStatusNames(packet.Buffer, packet.Buffer.Length, transactionId))
                {
                    if (name.Length > 0)
                    {
                        machines.Add(new LanMachine(name, packet.RemoteEndPoint.Address.ToString()));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                break;
            }
        }

        return machines;
    }

    private static IReadOnlyList<IPAddress> GetBroadcastAddresses()
    {
        var addresses = new HashSet<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                {
                    continue;
                }

                foreach (var unicast in ni.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork ||
                        unicast.Address.Equals(IPAddress.Loopback))
                    {
                        continue;
                    }

                    var mask = unicast.IPv4Mask;
                    if (mask is null)
                    {
                        continue;
                    }

                    var broadcast = FromUInt32(
                        ToUInt32(unicast.Address) | ~ToUInt32(mask));
                    if (!broadcast.Equals(unicast.Address))
                    {
                        addresses.Add(broadcast);
                    }
                }
            }
        }
        catch
        {
            // Fall back to the limited broadcast below.
        }

        addresses.Add(IPAddress.Broadcast);
        return addresses.ToArray();
    }

    private static byte[] BuildNodeStatusQuery(ushort transactionId)
    {
        var query = new List<byte>(50)
        {
            (byte)(transactionId >> 8), (byte)transactionId,
            0x00, 0x00, // flags
            0x00, 0x01, // one question
            0x00, 0x00, // no answers
            0x00, 0x00,
            0x00, 0x00,
            0x20, // encoded 16-byte NetBIOS name
        };

        for (var i = 0; i < 16; i++)
        {
            var value = i == 0 ? (byte)'*' : (byte)' ';
            query.Add((byte)('A' + (value >> 4)));
            query.Add((byte)('A' + (value & 0x0F)));
        }

        query.Add(0x00); // empty scope
        query.Add(0x00); query.Add(0x21); // QTYPE NBSTAT
        query.Add(0x00); query.Add(0x01); // QCLASS IN
        return query.ToArray();
    }

    private static IReadOnlyList<string> ParseNodeStatusNames(byte[] data, int length, ushort transactionId)
    {
        var names = new List<string>();
        if (length < 12 || ReadUInt16(data, 0) != transactionId ||
            (ReadUInt16(data, 2) & 0x8000) == 0 ||
            (ReadUInt16(data, 2) & 0x000F) != 0)
        {
            return names;
        }

        var questionCount = ReadUInt16(data, 4);
        var answerCount = ReadUInt16(data, 6);
        var offset = 12;
        for (var i = 0; i < questionCount; i++)
        {
            offset = SkipDnsName(data, offset, length) ?? length;
            if (offset + 4 > length)
            {
                return names;
            }

            offset += 4;
        }

        for (var i = 0; i < answerCount && offset < length; i++)
        {
            offset = SkipDnsName(data, offset, length) ?? length;
            if (offset + 10 > length)
            {
                break;
            }

            var type = ReadUInt16(data, offset);
            var recordLength = ReadUInt16(data, offset + 8);
            var recordStart = offset + 10;
            var recordEnd = recordStart + recordLength;
            if (recordEnd > length)
            {
                break;
            }

            if (type == 0x0021 && recordLength >= 1)
            {
                var count = data[recordStart];
                var nameOffset = recordStart + 1;
                for (var n = 0; n < count && nameOffset + 18 <= recordEnd; n++)
                {
                    var name = Encoding.ASCII.GetString(data, nameOffset, 15).Trim(' ', '\0');
                    var suffix = data[nameOffset + 15];
                    var flags = ReadUInt16(data, nameOffset + 16);
                    if (suffix == 0x00 && (flags & 0x8000) == 0 && IsUsableMachineName(name))
                    {
                        names.Add(name);
                    }

                    nameOffset += 18;
                }
            }

            offset = recordEnd;
        }

        return names;
    }

    private static int? SkipDnsName(byte[] data, int offset, int length)
    {
        var position = offset;
        while (position < length)
        {
            var labelLength = data[position++] & 0xFF;
            if (labelLength == 0)
            {
                return position;
            }

            if ((labelLength & 0xC0) == 0xC0)
            {
                return position < length ? position + 1 : null;
            }

            if (position + labelLength > length)
            {
                return null;
            }

            position += labelLength;
        }

        return null;
    }

    private static IReadOnlyList<LanMachine> MergeMachines(
        IEnumerable<LanMachine> first,
        IEnumerable<LanMachine> second)
    {
        var merged = new Dictionary<string, LanMachine>(StringComparer.OrdinalIgnoreCase);
        foreach (var machine in first.Concat(second))
        {
            if (!IsUsableMachineName(machine.Name))
            {
                continue;
            }

            if (!merged.TryGetValue(machine.Name, out var existing) ||
                (existing.Ip.Length == 0 && machine.Ip.Length > 0))
            {
                merged[machine.Name] = machine with { Name = machine.Name.Trim() };
            }
        }

        return merged.Values
            .OrderBy(machine => machine.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsUsableMachineName(string name)
    {
        var value = name.Trim();
        return value.Length is > 0 and <= 63 &&
               value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) |
               ((uint)bytes[1] << 16) |
               ((uint)bytes[2] << 8) |
               bytes[3];
    }

    private static IPAddress FromUInt32(uint value) => new(
        new[]
        {
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value,
        });

    private static ushort ReadUInt16(byte[] data, int offset) => (ushort)(
        ((data[offset] & 0xFF) << 8) | (data[offset + 1] & 0xFF));

    public static string FailureMessage(string host)
    {
        var baseMessage = $"无法解析 {host}：系统 DNS / NetBIOS 均无结果";
        return TailscaleActive()
            ? $"{baseMessage}。检测到 Tailscale：请确认 MagicDNS 已开启，或直接使用 IP 地址"
            : $"{baseMessage}。请确认与服务器在同一局域网，或使用 IP 地址";
    }
}
