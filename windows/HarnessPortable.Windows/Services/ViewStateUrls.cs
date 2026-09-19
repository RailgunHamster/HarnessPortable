using System.Globalization;
using HarnessPortable.Windows.Models;

namespace HarnessPortable.Windows.Services;

/// <summary>
/// Per-tab web view state exchanged with the <c>dsh-view-state</c> dsh plugin.
///
/// The dsh web UI has no router: the selected session, the sidebar fold state and
/// the panel widths live only in the page's memory, so a wrapper cannot observe
/// them. The plugin mirrors them into three query parameters on whatever path the
/// page is on, and re-applies them on load. We only ever read and write those
/// three keys — never the whole URL, because dsh's own startup URL carries a
/// process token and presets are plain files on disk.
///
/// Contract (see the plugin's README):
///   dsh_session=&lt;sessionId&gt;   current session, absent when there is none
///   dsh_sidebar=&lt;px&gt;           sidebar width, 0 = collapsed
///   dsh_rightbar=&lt;px&gt;          right panel's saved width, 0 = none recorded yet
///
/// The right panel's <em>visibility</em> is deliberately outside the contract:
/// it belongs to ui-sidebar-right's per-session store, which reports it to the
/// frame itself, so driving it from a preset would race that seat.
/// </summary>
public static class ViewStateUrls
{
    public const string SessionParam = "dsh_session";
    public const string SidebarParam = "dsh_sidebar";
    public const string RightbarParam = "dsh_rightbar";

    /// <summary>The three keys this app owns, in the order the plugin writes them.</summary>
    public static readonly string[] OwnedParams = [SessionParam, SidebarParam, RightbarParam];

    /// <summary>
    /// Reads the plugin's three parameters out of a page URL. Returns null when the
    /// URL is not an http(s) URL or carries none of them, so callers can keep the
    /// preset untouched instead of storing an empty blob.
    /// </summary>
    public static LayoutWebState? Extract(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        var session = Find(query, SessionParam);
        var sidebar = ParsePixels(Find(query, SidebarParam));
        var rightbar = ParsePixels(Find(query, RightbarParam));

        if (session is null && sidebar is null && rightbar is null)
        {
            return null;
        }

        return new LayoutWebState
        {
            SessionId = string.IsNullOrWhiteSpace(session) ? null : session,
            Sidebar = sidebar,
            Rightbar = rightbar,
        };
    }

    /// <summary>
    /// Returns <paramref name="url"/> with the state's parameters added or replaced.
    /// Every other parameter — notably dsh's own <c>token</c> — and the path are
    /// preserved byte for byte, and a null field is left alone rather than cleared
    /// (the plugin falls back to its stored copy for keys the URL does not carry).
    /// </summary>
    public static string Merge(string url, LayoutWebState? state)
    {
        if (state is null ||
            string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return url;
        }

        var pairs = ParseQuery(uri.Query);

        if (state.SessionId is { Length: > 0 } session)
        {
            Set(pairs, SessionParam, session);
        }

        if (state.Sidebar is { } sidebar)
        {
            Set(pairs, SidebarParam, sidebar.ToString(CultureInfo.InvariantCulture));
        }

        if (state.Rightbar is { } rightbar)
        {
            Set(pairs, RightbarParam, rightbar.ToString(CultureInfo.InvariantCulture));
        }

        var query = pairs.Count == 0
            ? string.Empty
            : "?" + string.Join("&", pairs.Select(p => p.Key + "=" + p.Value));

        var fragment = uri.Fragment; // includes the leading '#', or is empty
        return uri.GetLeftPart(UriPartial.Path) + query + fragment;
    }

    private static void Set(List<KeyValuePair<string, string>> pairs, string key, string value)
    {
        for (var i = 0; i < pairs.Count; i++)
        {
            if (string.Equals(pairs[i].Key, key, StringComparison.Ordinal))
            {
                pairs[i] = new KeyValuePair<string, string>(key, Uri.EscapeDataString(value));
                return;
            }
        }

        pairs.Add(new KeyValuePair<string, string>(key, Uri.EscapeDataString(value)));
    }

    private static string? Find(List<KeyValuePair<string, string>> pairs, string key)
    {
        foreach (var pair in pairs)
        {
            if (string.Equals(pair.Key, key, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(pair.Value);
            }
        }

        return null;
    }

    private static int? ParsePixels(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var px) && px >= 0
            ? px
            : null;

    /// <summary>Query pairs as written, so untouched parameters survive a round trip.</summary>
    private static List<KeyValuePair<string, string>> ParseQuery(string query)
    {
        var pairs = new List<KeyValuePair<string, string>>();

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = part.IndexOf('=');
            pairs.Add(at < 0
                ? new KeyValuePair<string, string>(part, string.Empty)
                : new KeyValuePair<string, string>(part[..at], part[(at + 1)..]));
        }

        return pairs;
    }
}

/// <summary>One tab's captured web view state; null fields mean "not recorded".</summary>

