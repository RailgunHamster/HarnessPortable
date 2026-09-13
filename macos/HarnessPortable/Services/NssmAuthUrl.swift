import Foundation

/// Obtains the current "dsh web" launch-token URL from the SSH peer when the
/// service there runs under NSSM. dsh prints its authenticated root URL to
/// stdout once at startup; NSSM's configured stdout redirection persists that
/// line to a log file, and `nssm get <service> AppStdout` names the file.
/// The script below discovers the dsh service, extracts the newest printed
/// URL and verifies it against the live server (expecting the 303
/// token-redirect) so only a currently-valid URL is returned. Failures
/// return nil and callers fall back to the plain host:port URL (whose 401
/// page opens the manual paste sheet).
enum NssmAuthUrl {

    private static let fetchTimeout: TimeInterval = 10

    /// Exit codes: 0 = verified URL on stdout, 3 = not found, 4 = stale token.
    static let script = """
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
foreach ($c in (Get-CimInstance Win32_Service | Where-Object { $_.State -eq "Running" -and $_.PathName -match "nssm\\.exe" })) {
  $exe = [regex]::Match($c.PathName, "^""?(.+?nssm\\.exe)").Groups[1].Value
  if (-not $exe) { continue }
  $app = & $exe get $c.Name Application 2>$null
  $params = & $exe get $c.Name AppParameters 2>$null
  if ("$app $params" -notmatch "dsh") { continue }
  $log = & $exe get $c.Name AppStdout 2>$null
  if (-not $log) { continue }
  $m = Select-String -Path "$log" -Pattern "dsh web: (http\\S+)" | Select-Object -Last 1
  if (-not $m) { continue }
  $u = $m.Matches[0].Groups[1].Value
  $code = & curl.exe -s -o NUL -w "%{http_code}" --max-time 3 $u
  if ("$code" -eq "303") { Write-Output $u; exit 0 }
  exit 4
}
exit 3
"""

    /// Last absolute http(s) URL appearing in the output, if any.
    static func extractUrl(from output: String) -> String? {
        let regex = try? NSRegularExpression(pattern: #"https?://\S+"#)
        guard let regex else { return nil }
        let ns = output as NSString
        var last: String?
        regex.enumerateMatches(
            in: output, range: NSRange(location: 0, length: ns.length)
        ) { match, _, _ in
            if let match {
                last = ns.substring(with: match.range)
            }
        }
        return last
    }

    /// True when a page body looks like the dsh "reopen the URL" rejection.
    static func looksLikeAuthRequired(_ text: String) -> Bool {
        text.localizedCaseInsensitiveContains("dsh web authentication required")
            || (text.localizedCaseInsensitiveContains("authentication required")
                && text.localizedCaseInsensitiveContains("reopen the url"))
    }

    /// Port component of an absolute URL, or nil when absent/unparseable.
    static func port(of url: String) -> Int? {
        URLComponents(string: url)?.port
    }

    /// Rewrites an authenticated URL so WKWebView opens it through the local
    /// forwarded port: keeps path + token query, replaces the authority with
    /// 127.0.0.1:localPort. Pass 0 as expectedRemotePort to skip the
    /// source-port sanity check.
    static func rewriteToLocal(_ authUrl: String?, localPort: Int, expectedRemotePort: Int) -> String? {
        guard let authUrl = authUrl?.trimmingCharacters(in: .whitespacesAndNewlines),
              !authUrl.isEmpty,
              let components = URLComponents(string: authUrl),
              let scheme = components.scheme?.lowercased(),
              scheme == "http" || scheme == "https" else {
            return nil
        }
        if expectedRemotePort > 0, components.port != expectedRemotePort {
            return nil
        }
        var local = URLComponents()
        local.scheme = "http"
        local.host = "127.0.0.1"
        local.port = localPort
        local.path = components.path.isEmpty ? "/" : components.path
        local.query = components.query
        return local.url?.absoluteString
    }

    /// Builds the URL to open from a user-pasted input against the base URL
    /// currently loaded in the web view. Accepted inputs: a full URL (its
    /// path + query replace the base's), a bare query ("?token=..."), a
    /// key=value pair, or a bare token. Returns nil when nothing usable was
    /// pasted.
    static func buildAuthTarget(base: String, input: String) -> String? {
        let trimmed = input.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return nil }

        let strippedBase = (base.components(separatedBy: "?").first ?? base).trimmingCharacters(
            in: CharacterSet(charactersIn: "/")
        )

        if let components = URLComponents(string: trimmed),
           let scheme = components.scheme?.lowercased(),
           scheme == "http" || scheme == "https" {
            let path = components.path.isEmpty ? "/" : components.path
            let query = components.query.map { "?\($0)" } ?? ""
            let pathAndQuery = path + query
            guard pathAndQuery.contains("?") else { return nil }
            return strippedBase + pathAndQuery
        }

        if trimmed.hasPrefix("?") {
            return strippedBase + trimmed
        }

        if trimmed.contains("="), !trimmed.contains("/"), !trimmed.contains(" ") {
            return strippedBase + "/?" + trimmed
        }

        // A bare token: no path separator, no whitespace.
        if !trimmed.contains("/"), trimmed.rangeOfCharacter(from: .whitespacesAndNewlines) == nil {
            return strippedBase + "/?token=" + WebAuthInput.percentEncodeTokenValue(trimmed)
        }

        return nil
    }

    /// Runs the discovery script over a one-shot `ssh` process using the same
    /// askpass/known-hosts plumbing as the tunnel itself. Blocking; call from
    /// a background queue. Returns the verified absolute URL, or nil on any
    /// failure/timeout. Never throws.
    static func fetch(
        user: String,
        host: String,
        port: Int,
        knownHostsFile: URL,
        passwordFile: URL,
        askpass: URL
    ) -> String? {
        guard let encoded = script.data(using: .utf16LittleEndian)?.base64EncodedString() else {
            return nil
        }

        let command = Process()
        command.executableURL = URL(fileURLWithPath: "/usr/bin/ssh")
        let escapedKnownHostsPath = knownHostsFile.path.replacingOccurrences(of: "\"", with: "\\\"")
        let knownHostsOption = "UserKnownHostsFile=\"\(escapedKnownHostsPath)\""
        command.arguments = [
            "-4",
            "-o", "StrictHostKeyChecking=yes",
            "-o", knownHostsOption,
            "-o", "PreferredAuthentications=keyboard-interactive,password",
            "-o", "PubkeyAuthentication=no",
            "-o", "NumberOfPasswordPrompts=1",
            "-o", "ConnectTimeout=20",
            "-p", String(port),
            "\(user)@\(host)",
            "powershell -NoProfile -EncodedCommand \(encoded)"
        ]

        var environment = ProcessInfo.processInfo.environment
        environment["SSH_ASKPASS"] = askpass.path
        environment["SSH_ASKPASS_REQUIRE"] = "force"
        environment["DISPLAY"] = "1"
        environment["HARNESS_PASSWORD_FILE"] = passwordFile.path
        command.environment = environment
        command.standardInput = FileHandle.nullDevice
        command.standardError = FileHandle.nullDevice
        let output = Pipe()
        command.standardOutput = output

        do {
            try command.run()
        } catch {
            return nil
        }

        let semaphore = DispatchSemaphore(value: 0)
        DispatchQueue.global().async {
            command.waitUntilExit()
            semaphore.signal()
        }
        if semaphore.wait(timeout: .now() + fetchTimeout) == .timedOut {
            command.terminate()
            return nil
        }

        guard command.terminationStatus == 0 else { return nil }
        let data = output.fileHandleForReading.readDataToEndOfFile()
        let text = String(data: data, encoding: .utf8) ?? ""
        return extractUrl(from: text)
    }
}
