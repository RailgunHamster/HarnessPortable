using System.Text.Json.Nodes;
using System.IO;

namespace HarnessPortable.Windows.Services;

/// <summary>The credential-free, versioned wire format shared by all three clients.</summary>
public static class SharedConfiguration
{
    public const int Limit = 1024 * 1024;
    public const string FileName = "harness-settings-v1.json";
    public static System.Text.Json.JsonSerializerOptions JsonOptions { get; } = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static JsonObject Empty() => new() { ["format"] = "harness-portable-settings", ["version"] = 1, ["entries"] = new JsonObject() };
    public static JsonObject Entries(JsonObject document) => (JsonObject)document["entries"]!;
    public static bool Same(JsonNode? left, JsonNode? right) => JsonNode.DeepEquals(left, right);
    public static string PortableUrl(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
            throw new InvalidDataException("直连地址不是有效的 HTTP(S) 地址");
        if (uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0) return raw;
        return new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" }.Uri.AbsoluteUri;
    }
    private static readonly string[] TunnelFields = ["id", "name", "sshHost", "sshPort", "user", "remoteHost", "remotePort", "localPort", "authMode", "systemdUnit", "systemdScope", "systemdLogPath", "favorite", "group", "label", "rank"];
    private static readonly string[] DirectFields = ["url", "favorite", "group", "label", "rank"];
    public static readonly HashSet<string> BooleanSettings = ["windows:restoreLastLayoutOnStartup", "windows:reconnectRestoredWorkspace", "windows:connectionSidebarCollapsed", "macos:restoreLastLayoutOnStartup", "macos:reconnectRestoredWorkspace", "macos:connectionSidebarCollapsed", "android:browserFullscreen"];
    public static void Validate(JsonObject document)
    {
        Require(document.Count == 3 && document["format"]?.ToString() == "harness-portable-settings" && document["version"] is JsonValue v && v.TryGetValue<int>(out var version) && version == 1, "不支持的同步文件格式");
        Require(System.Text.Encoding.UTF8.GetByteCount(document.ToJsonString(JsonOptions)) <= Limit && document["entries"] is JsonObject, "同步文件无效或过大");
        var entries = Entries(document);
        Require(entries.Count <= 2000, "同步项目超过上限");
        foreach (var (key, value) in entries)
        {
            Require(key.Length <= 9000 && !key.Any(char.IsControl), "同步项目标识无效");
            if (key.StartsWith("tunnel:") || key.StartsWith("direct:"))
            {
                Require(value is JsonObject, "连接信息无效"); var item = (JsonObject)value!;
                var tunnel = key.StartsWith("tunnel:"); var fields = tunnel ? TunnelFields : DirectFields;
                Require(item.Count == fields.Length && item.All(p => fields.Contains(p.Key)), "连接含未知或敏感字段");
                foreach (var field in fields.Except(["favorite", "rank", "sshPort", "remotePort", "localPort"]))
                    Require(item[field] is JsonValue text && text.TryGetValue<string>(out var s) && s.Length <= 8192 && !s.Any(char.IsControl), "连接字段无效");
                Require(item["favorite"] is JsonValue favorite && favorite.TryGetValue<bool>(out _), "收藏标记无效");
                Require(item["rank"] is JsonValue rank && rank.TryGetValue<int>(out var r) && r is >= 0 and <= 1000000, "排序无效");
                if (tunnel)
                {
                    Require(key == "tunnel:" + item["id"] && System.Text.RegularExpressions.Regex.IsMatch(item["id"]!.ToString(), "^[A-Za-z0-9_-]{1,120}$") && !string.IsNullOrWhiteSpace(item["sshHost"]!.ToString()) && !string.IsNullOrWhiteSpace(item["user"]!.ToString()), "SSH 标识无效");
                    foreach (var port in new[] { "sshPort", "remotePort", "localPort" })
                        Require(item[port] is JsonValue p && p.TryGetValue<int>(out var n) && n is >= 1 and <= 65535, "SSH 端口无效");
                    Require(new[] { "nssm", "manual", "systemd", "launchd" }.Contains(item["authMode"]!.ToString()), "认证模式无效");
                }
                else Require(key == "direct:" + item["url"] && PortableUrl(item["url"]!.ToString()) == item["url"]!.ToString(), "直连地址含凭据或登录参数");
            }
            else if (BooleanSettings.Contains(key)) Require(value is JsonValue b && b.TryGetValue<bool>(out _), "偏好类型无效");
            else
            {
                var choices = key switch { "shared:appearance" => new[] { "system", "light", "dark" }, "android:orientation" => ["auto", "portrait", "landscape"], "windows:closeBehavior" or "macos:closeBehavior" => ["exit", "tray"], _ => [] };
                Require(value is JsonValue s && s.TryGetValue<string>(out var text) && choices.Contains(text), "未知或无效的同步偏好");
            }
        }
    }
    public static (JsonObject Document, string[] Conflicts) Merge(JsonObject baseline, JsonObject local, JsonObject remote, string? choice = null)
    {
        Validate(baseline); Validate(local); Validate(remote);
        var result = Empty(); var output = Entries(result); var conflicts = new List<string>();
        var b = Entries(baseline); var l = Entries(local); var r = Entries(remote);
        foreach (var key in b.Select(x => x.Key).Union(l.Select(x => x.Key)).Union(r.Select(x => x.Key)).Order(StringComparer.Ordinal))
        {
            var bv = b[key]; var lv = l[key]; var rv = r[key]; JsonNode? selected;
            if (Same(lv, rv)) selected = lv;
            else if (Same(lv, bv)) selected = rv;
            else if (Same(rv, bv)) selected = lv;
            else { conflicts.Add(key); selected = choice == "remote" ? rv : lv; }
            if (selected != null) output[key] = selected.DeepClone();
        }
        Validate(result); return (result, conflicts.ToArray());
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
}
