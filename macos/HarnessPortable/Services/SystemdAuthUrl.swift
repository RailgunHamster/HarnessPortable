import Foundation

/// The shared Linux helper runs through the tunnel's existing SSH identity plumbing.
enum SystemdAuthUrl {
    static func shellQuote(_ value: String) -> String {
        "'" + value.replacingOccurrences(of: "'", with: "'\"'\"'") + "'"
    }

    static func command(for profile: TunnelProfile) -> String? {
        guard let resource = Bundle.main.url(forResource: "systemd-auth", withExtension: "sh"),
              let text = try? String(contentsOf: resource, encoding: .utf8) else { return nil }
        let script = text.replacingOccurrences(of: "\r\n", with: "\n")
        let unit = profile.systemdUnit.trimmingCharacters(in: .whitespacesAndNewlines)
        return "sh -c " + shellQuote(script) + " sh " + shellQuote(unit.isEmpty ? "dsh-web.service" : unit)
            + " " + shellQuote(profile.systemdScope) + " " + String(profile.remotePort)
            + " " + shellQuote(profile.systemdLogPath.trimmingCharacters(in: .whitespacesAndNewlines))
    }

    static func fetch(
        profile: TunnelProfile, host: String, knownHostsFile: URL,
        passwordFile: URL?, askpass: URL
    ) -> String? {
        guard let command = command(for: profile),
              let output = NssmAuthUrl.runSSHCommand(
                user: profile.user, host: host, port: profile.sshPort,
                knownHostsFile: knownHostsFile, passwordFile: passwordFile,
                askpass: askpass, identityFile: profile.identityFile,
                remoteCommand: command
              ), let url = NssmAuthUrl.extractUrl(from: output),
              NssmAuthUrl.rewriteToLocal(url, localPort: profile.localPort,
                                       expectedRemotePort: profile.remotePort) != nil else { return nil }
        return url
    }
}
