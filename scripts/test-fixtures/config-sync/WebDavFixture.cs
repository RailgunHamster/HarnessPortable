using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HarnessPortable.Testing;

/// <summary>Disposable loopback DAV endpoint with real conditional GET/PUT semantics.</summary>
public sealed class WebDavFixture : IAsyncDisposable
{
    public sealed record Request(string Method, string Path, Dictionary<string, string> Headers, string Body);
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Task _loop;
    private string? _document;
    private int _revision;
    public string? ETagOverride { get; set; }
    public bool OmitETag { get; set; }
    public string? Redirect { get; set; }
    public int RejectCode { get; set; }
    public Action? BeforeNextPut;
    public List<Request> Requests { get; } = [];
    public string Url => "http://127.0.0.1:" + ((IPEndPoint)_listener.LocalEndpoint).Port + "/dav/Harness/";
    public string? Document { get { lock (_gate) return _document; } set { lock (_gate) { _document = value; _revision++; } } }
    public WebDavFixture() { _listener.Start(); _loop = Task.Run(Run); }
    private async Task Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
            using var stream = client.GetStream();
            try
            {
                using var header = new MemoryStream(); var tail = 0;
                while (header.Length < 16384)
                {
                    var one = new byte[1]; if (await stream.ReadAsync(one, _stop.Token) == 0) break;
                    header.WriteByte(one[0]); tail = (tail << 8) | one[0]; if (tail == 0x0d0a0d0a) break;
                }
                var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n"); var first = lines[0].Split(' ');
                if (first.Length < 2) continue;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in lines.Skip(1)) { var i = line.IndexOf(':'); if (i > 0) headers[line[..i]] = line[(i + 1)..].Trim(); }
                var length = headers.TryGetValue("Content-Length", out var text) ? int.Parse(text) : 0;
                if (length > 2 * 1024 * 1024) throw new IOException("Fixture request too large");
                var body = new byte[length]; await stream.ReadExactlyAsync(body, _stop.Token);
                var request = new Request(first[0], first[1], headers, Encoding.UTF8.GetString(body));
                if (request.Method == "PUT") Interlocked.Exchange(ref BeforeNextPut, null)?.Invoke();
                int code; string response; string etag;
                lock (_gate)
                {
                    Requests.Add(request); etag = ETagOverride ?? "\"v" + _revision + "\"";
                    if (RejectCode != 0) { code = RejectCode; response = ""; }
                    else if (Redirect != null) { code = 302; response = ""; }
                    else if (request.Method == "PROPFIND") { code = 207; response = "<multistatus/>"; }
                    else if (request.Method == "GET") { code = _document == null ? 404 : 200; response = _document ?? ""; }
                    else if (request.Method == "PUT")
                    {
                        var matches = _document == null ? headers.GetValueOrDefault("If-None-Match") == "*" : headers.GetValueOrDefault("If-Match") == "\"v" + _revision + "\"";
                        if (!matches) { code = 412; response = ""; }
                        else { _document = request.Body; _revision++; etag = "\"v" + _revision + "\""; code = 204; response = ""; }
                    }
                    else { code = 405; response = ""; }
                }
                var bytes = Encoding.UTF8.GetBytes(response);
                var head = $"HTTP/1.1 {code} Fixture\r\nConnection: close\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\n";
                if (!OmitETag) head += "ETag: " + etag + "\r\n";
                if (Redirect != null) head += "Location: " + Redirect + "\r\n";
                await stream.WriteAsync(Encoding.ASCII.GetBytes(head + "\r\n"), _stop.Token); await stream.WriteAsync(bytes, _stop.Token);
            }
            catch (Exception e) when (e is IOException or OperationCanceledException) { }
        }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); _listener.Stop(); try { await _loop; } catch (OperationCanceledException) { } catch (SocketException) { } _stop.Dispose();
    }
}
