import Foundation

/// Per-tab web view state exchanged with the `dsh-view-state` dsh plugin.
///
/// The dsh web UI has no router: the selected session, the sidebar fold state
/// and the panel widths live only in the page's memory, so a wrapper cannot
/// observe them. The plugin mirrors them into three query parameters on
/// whatever path the page is on, and re-applies them on load. We only ever
/// read and write those three keys — never the whole URL, because dsh's own
/// startup URL carries a process token and presets are plain files on disk.
///
/// Contract (see the plugin's README):
///   dsh_session=<sessionId>   current session, absent when there is none
///   dsh_sidebar=<px>          sidebar width, 0 = collapsed
///   dsh_rightbar=<px>         right panel's saved width, 0 = none recorded yet
///
/// The right panel's *visibility* is deliberately outside the contract: it
/// belongs to ui-sidebar-right's per-session store, so driving it from a
/// preset would race that seat.
enum ViewStateUrls {
    static let sessionParam = "dsh_session"
    static let sidebarParam = "dsh_sidebar"
    static let rightbarParam = "dsh_rightbar"

    /// Reads the plugin's three parameters out of a page URL. Returns nil when
    /// the URL is not an http(s) URL or carries none of them, so callers can
    /// keep the preset untouched instead of storing an empty blob.
    static func extract(_ url: String?) -> LayoutWebState? {
        guard let url,
              let components = httpComponents(url) else { return nil }

        var session: String?
        var sidebar: Int?
        var rightbar: Int?
        for (key, value) in parseQuery(components.percentEncodedQuery ?? "") {
            switch key {
            case sessionParam:
                let decoded = unescape(value)
                session = decoded.isEmpty ? nil : decoded
            case sidebarParam: sidebar = pixels(unescape(value))
            case rightbarParam: rightbar = pixels(unescape(value))
            default: break
            }
        }

        guard session != nil || sidebar != nil || rightbar != nil else { return nil }
        return LayoutWebState(sessionId: session, sidebar: sidebar, rightbar: rightbar)
    }

    /// Returns `url` with the state's parameters added or replaced. Every other
    /// parameter — notably dsh's own `token` — and the path are preserved as
    /// written, and a nil field is left alone rather than cleared (the plugin
    /// falls back to its stored copy for keys the URL does not carry).
    static func merge(_ url: String, state: LayoutWebState?) -> String {
        guard let state,
              let components = httpComponents(url) else { return url }

        var pairs = parseQuery(components.percentEncodedQuery ?? "")

        if let session = state.sessionId, !session.isEmpty {
            set(&pairs, sessionParam, escape(session))
        }
        if let sidebar = state.sidebar {
            set(&pairs, sidebarParam, String(sidebar))
        }
        if let rightbar = state.rightbar {
            set(&pairs, rightbarParam, String(rightbar))
        }

        var output = components
        output.percentEncodedQuery = pairs.isEmpty ? nil : pairs.map { "\($0.0)=\($0.1)" }.joined(separator: "&")
        return output.url?.absoluteString ?? url
    }

    /// Whether a loaded page still needs the recorded state applied to it.
    ///
    /// dsh's startup URL carries a one-shot `token` and answers with a redirect
    /// to the clean root, and that redirect drops the query string — so state
    /// merged into the token URL never reaches the page, and the plugin (which
    /// reads its parameters at boot only) never sees it. Once the real page is
    /// up, this says whether to put the state back with one extra navigation:
    /// true when the page is http(s) and carries none of the plugin's parameters.
    static func needsReapply(_ liveURL: String?) -> Bool {
        guard let liveURL,
              let components = httpComponents(liveURL) else { return false }
        var owned = false
        for (key, _) in parseQuery(components.percentEncodedQuery ?? "") {
            if key == sessionParam || key == sidebarParam || key == rightbarParam {
                owned = true
                break
            }
        }
        return !owned
    }

    // MARK: - Internals

    private static func httpComponents(_ url: String) -> URLComponents? {
        guard let components = URLComponents(string: url.trimmingCharacters(in: .whitespacesAndNewlines)),
              let scheme = components.scheme?.lowercased(),
              scheme == "http" || scheme == "https",
              components.host != nil else { return nil }
        return components
    }

    private static func pixels(_ raw: String) -> Int? {
        guard let value = Int(raw), value >= 0 else { return nil }
        return value
    }

    private static func set(_ pairs: inout [(String, String)], _ key: String, _ value: String) {
        for index in pairs.indices where pairs[index].0 == key {
            pairs[index] = (key, value)
            return
        }
        pairs.append((key, value))
    }

    private static func unescape(_ value: String) -> String {
        value.removingPercentEncoding ?? value
    }

    /// Percent-encodes everything outside the unreserved set, matching the
    /// plugin's URLSearchParams round trip for values like "session-a b&c".
    private static func escape(_ value: String) -> String {
        let unreserved = CharacterSet.alphanumerics.union(CharacterSet(charactersIn: "-._~"))
        return value.addingPercentEncoding(withAllowedCharacters: unreserved) ?? value
    }

    /// Query pairs as written, so untouched parameters survive a round trip.
    private static func parseQuery(_ query: String) -> [(String, String)] {
        let trimmed = query.hasPrefix("?") ? String(query.dropFirst()) : query
        guard !trimmed.isEmpty else { return [] }
        return trimmed.split(separator: "&", omittingEmptySubsequences: true).map { part in
            guard let at = part.firstIndex(of: "=") else { return (String(part), "") }
            return (String(part[..<at]), String(part[part.index(after: at)...]))
        }
    }
}
