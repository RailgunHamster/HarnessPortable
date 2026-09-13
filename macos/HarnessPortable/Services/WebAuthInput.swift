import Foundation

/// Turns what the user typed (a dsh web token URL, a bare query, a
/// key=value pair, or the token itself) into the absolute URL the web view
/// should open. Returns nil when nothing usable was typed.
enum WebAuthInput {
    static func normalize(_ input: String?, host: String, port: Int) -> String? {
        guard let input else { return nil }
        let trimmed = input.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return nil }

        let trimmedHost = host.trimmingCharacters(in: .whitespacesAndNewlines)
        let authority = "http://\(trimmedHost.isEmpty ? "127.0.0.1" : trimmedHost):\(port)"

        // A full URL is only usable when it carries a token query; the bare
        // host:port URL would just answer 401 again.
        if let components = URLComponents(string: trimmed),
           let scheme = components.scheme?.lowercased(),
           scheme == "http" || scheme == "https" {
            guard let query = components.query, !query.isEmpty else { return nil }
            return trimmed
        }

        if trimmed.hasPrefix("?") {
            return authority + trimmed
        }

        let hasWhitespace = trimmed.rangeOfCharacter(from: .whitespacesAndNewlines) != nil

        if trimmed.contains("="), !trimmed.contains("/"), !hasWhitespace {
            return authority + "/?" + trimmed
        }

        // A bare token: dsh tokens are base64url, so anything without a
        // query, a path separator or whitespace is taken as the token value.
        if !trimmed.contains("?"), !trimmed.contains("/"), !hasWhitespace {
            return authority + "/?token=" + percentEncodeTokenValue(trimmed)
        }

        return nil
    }

    /// Minimal escaping for a query value: only `[A-Za-z0-9_-]` survive.
    static func percentEncodeTokenValue(_ value: String) -> String {
        let allowed = CharacterSet(
            charactersIn: "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-"
        )
        return value.addingPercentEncoding(withAllowedCharacters: allowed) ?? value
    }
}
