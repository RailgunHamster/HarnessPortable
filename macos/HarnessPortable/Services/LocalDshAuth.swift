import Foundation

/// Auto-login for **direct** tabs pointing at a `dsh web` that runs on this
/// very Mac under the `com.deepseek.dsh-web` Launch Agent. The tunnel modes
/// (NSSM / Launch Agent over SSH) do not apply to a direct URL — there is no
/// SSH hop — so this reads the local stdout log and grafts the newest token
/// onto the direct destination. Invalid or missing tokens leave the URL
/// untouched and the existing 401 paste sheet takes over.
enum LocalDshAuth {
    static let logPath = "/tmp/dsh-web.stdout.log"
    static let modeNone = "none"
    static let modeLaunchd = "launchd"

    /// Builds the authenticated target for a direct destination from the
    /// launchd log text: takes the newest http(s) URL in the log, keeps only
    /// its query (the token), and replaces the query's token on `destination`.
    /// Path, host and every other parameter of the destination are preserved.
    static func authTarget(destination: String, logText: String?) -> String? {
        guard let newest = NssmAuthUrl.extractUrl(from: logText ?? ""),
              let source = URLComponents(string: newest),
              let query = source.query,
              query.contains("token="),
              let tokenItems = URLComponents(string: "?" + query)?.queryItems,
              !tokenItems.isEmpty,
              var target = URLComponents(string: destination),
              let targetHost = target.host, let sourceHost = source.host,
              HostResolver.isLocalHost(targetHost), HostResolver.isLocalHost(sourceHost),
              ["http", "https"].contains(target.scheme?.lowercased() ?? ""),
              (target.port ?? (target.scheme == "https" ? 443 : 80)) == (source.port ?? (source.scheme == "https" ? 443 : 80)) else {
            return nil
        }

        var items = target.queryItems ?? []
        items.removeAll { $0.name == "token" }
        items.append(contentsOf: tokenItems)
        target.queryItems = items.isEmpty ? nil : items
        return target.url?.absoluteString
    }

    /// Current log contents, or nil when the file is absent/unreadable.
    static func currentLogText() -> String? {
        try? String(contentsOfFile: logPath, encoding: .utf8)
    }
}
