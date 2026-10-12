namespace HarnessPortable.Windows.Services;

public static class DiagnosticUrls
{
    public static string Redact(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "[invalid URL]";
        if (uri.Scheme is not ("http" or "https")) return uri.Scheme + ":…";
        var builder = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
        return builder.Uri.GetLeftPart(UriPartial.Path) + (string.IsNullOrEmpty(uri.Query) ? "" : "?…");
    }
}
