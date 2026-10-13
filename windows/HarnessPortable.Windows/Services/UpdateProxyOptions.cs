using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using HarnessPortable.Windows.Models;
using Velopack.Sources;

namespace HarnessPortable.Windows.Services;

public sealed record UpdateProxyOptions(bool Enabled = false, string Address = UpdateProxyOptions.DefaultAddress)
{
    public const string DefaultAddress = "localhost:7890";

    public static UpdateProxyOptions From(AppSettings settings) => new(settings.UpdateProxyEnabled,
        string.IsNullOrWhiteSpace(settings.UpdateProxyAddress) ? DefaultAddress : settings.UpdateProxyAddress.Trim());

    public static bool TryParseAddress(string? address, out Uri? endpoint)
    {
        endpoint = null;
        var value = address?.Trim() ?? "";
        if (!value.Contains("://")) value = "http://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "http" ||
            string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            !Regex.IsMatch(value, @":\d{1,5}/?$") || uri.Port is < 1 or > 65535 || value.Any(char.IsWhiteSpace)) return false;
        endpoint = uri;
        return true;
    }

    public HttpClientHandler CreateHandler()
    {
        var handler = new HttpClientHandler();
        try { return ConfigureHandler(handler); }
        catch { handler.Dispose(); throw; }
    }

    internal HttpClientHandler ConfigureHandler(HttpClientHandler handler)
    {
        // Explicitly bypass system/environment proxies while the preference is off.
        handler.UseProxy = Enabled;
        if (!Enabled) return handler;
        if (!TryParseAddress(Address, out var endpoint))
        {
            throw new ArgumentException("代理地址无效，请填写 HTTP 代理地址，例如 localhost:7890。");
        }
        handler.Proxy = new WebProxy(endpoint!, false);
        return handler;
    }
}

public sealed class UpdateFileDownloader(UpdateProxyOptions proxy) : HttpClientFileDownloader
{
    protected override HttpClientHandler CreateHttpClientHandler()
    {
        var handler = base.CreateHttpClientHandler();
        try { return proxy.ConfigureHandler(handler); }
        catch { handler.Dispose(); throw; }
    }
}
