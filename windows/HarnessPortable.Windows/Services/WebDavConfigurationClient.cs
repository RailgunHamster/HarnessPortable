using System.Net;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace HarnessPortable.Windows.Services;

public sealed record WebDavOptions(string Url = "", string Username = "", bool AutoSync = false, bool AllowHttp = false);
public sealed class WebDavConfigurationClient : IDisposable
{
    private readonly HttpClient _http;
    public WebDavConfigurationClient(HttpMessageHandler? handler = null)
    {
        _http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(25) };
    }
    public static Uri Folder(WebDavOptions options)
    {
        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") ||
            string.IsNullOrEmpty(url.Host) || url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0 ||
            (url.Scheme == "http" && !options.AllowHttp) || options.Username.Contains(':') || options.Username.Any(char.IsControl))
            throw new InvalidDataException("请填写 HTTPS WebDAV 目录；可信局域网 HTTP 需明确允许，地址不能包含账号、密码或查询参数。");
        return new Uri(url.AbsoluteUri.TrimEnd('/') + "/");
    }
    private static HttpRequestMessage Request(WebDavOptions options, string password, HttpMethod method, bool folder = false)
    {
        var root = Folder(options);
        var request = new HttpRequestMessage(method, folder ? root : new Uri(root, SharedConfiguration.FileName));
        request.Headers.Accept.ParseAdd("application/json");
        if (options.Username.Length > 0 || password.Length > 0)
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(options.Username + ":" + password)));
        return request;
    }
    public async Task TestAsync(WebDavOptions options, string password, CancellationToken token)
    {
        using var request = Request(options, password, new HttpMethod("PROPFIND"), true);
        request.Headers.Add("Depth", "0");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        Check(response);
    }
    public async Task<(JsonObject Document, string? ETag, bool Missing)> ReadAsync(WebDavOptions options, string password, CancellationToken token)
    {
        using var request = Request(options, password, HttpMethod.Get);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return (SharedConfiguration.Empty(), null, true);
        Check(response);
        if (response.Content.Headers.ContentLength > SharedConfiguration.Limit) throw new InvalidDataException("同步文件超过 1 MiB");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var data = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (data.Length + read > SharedConfiguration.Limit) throw new InvalidDataException("同步文件超过 1 MiB");
            data.Write(buffer, 0, read);
        }
        var document = JsonNode.Parse(data.ToArray(), documentOptions: new() { MaxDepth = 12 }) as JsonObject ?? throw new InvalidDataException("同步文件不是 JSON 对象");
        SharedConfiguration.Validate(document);
        return (document, response.Headers.ETag?.ToString(), false);
    }
    public async Task<bool> WriteAsync(WebDavOptions options, string password, JsonObject document, string? etag, bool missing, CancellationToken token)
    {
        SharedConfiguration.Validate(document);
        if (!missing && (string.IsNullOrWhiteSpace(etag) || !EntityTagHeaderValue.TryParse(etag, out var tag) || tag.IsWeak || tag.Tag == "*"))
            throw new InvalidDataException("服务器未提供强 ETag，已停止上传，避免覆盖其他设备的修改。");
        using var request = Request(options, password, HttpMethod.Put);
        request.Headers.TryAddWithoutValidation(missing ? "If-None-Match" : "If-Match", missing ? "*" : etag);
        var bytes = Encoding.UTF8.GetBytes(document.ToJsonString());
        if (bytes.Length > SharedConfiguration.Limit) throw new InvalidDataException("同步文件超过 1 MiB");
        request.Content = new ByteArrayContent(bytes); request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/json; charset=utf-8");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.PreconditionFailed) return false;
        Check(response); return true;
    }
    private static void Check(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InvalidDataException((int)response.StatusCode switch
        {
            401 => "WebDAV 登录失败（401），请检查同步账号。", 403 => "WebDAV 拒绝访问（403），请检查目录权限。",
            404 or 409 => "WebDAV 目录不存在，请先创建私有同步目录。",
            >= 300 and < 400 => "WebDAV 返回重定向，请填写最终地址；未向重定向地址转发凭据。",
            var code => $"WebDAV 请求失败（HTTP {code}）。"
        });
    }
    public void Dispose() => _http.Dispose();
}
