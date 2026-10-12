using System.Net;
using System.Net.NetworkInformation;

namespace HarnessPortable.Windows.Services;

public static class LocalServiceIdentity
{
    public static bool IsLocalHost(string host)
    {
        host = host.Trim('[', ']').TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(host, out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Any(local => local.Address.Equals(address));
        }
        catch { return false; }
    }
}
