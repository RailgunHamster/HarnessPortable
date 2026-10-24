using System.Security.Cryptography;
using System.Text;
using HarnessPortable.Windows.Services;

namespace HarnessPortable.Windows.Automation;

public static class AutomationKeys
{
    public static string Connection(ConnectionEntry entry) => entry.Url is null ? entry.Key :
        "direct:"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Url))).ToLowerInvariant();
}
