import Foundation

struct TunnelProfile: Codable, Identifiable, Hashable {
    /// On connect, locate the dsh web launch-token URL via NSSM on the server (default).
    static let authModeNssm = "nssm"

    /// Let the user paste the token URL when the page rejects (fallback: 401 sheet).
    static let authModeManual = "manual"

    var id: String
    var name: String
    var sshHost: String
    var sshPort: Int
    var user: String
    var remoteHost: String
    var remotePort: Int
    var localPort: Int
    var authMode: String
    /// Optional OpenSSH private key path. Empty: default ~/.ssh identities and ssh-agent.
    var identityFile: String

    init(
        id: String = UUID().uuidString,
        name: String = "",
        sshHost: String = "",
        sshPort: Int = 22,
        user: String = "",
        remoteHost: String = "127.0.0.1",
        remotePort: Int = 3080,
        localPort: Int = 3080,
        authMode: String = TunnelProfile.authModeNssm,
        identityFile: String = ""
    ) {
        self.id = id
        self.name = name
        self.sshHost = sshHost
        self.sshPort = sshPort
        self.user = user
        self.remoteHost = remoteHost
        self.remotePort = remotePort
        self.localPort = localPort
        self.authMode = authMode
        self.identityFile = identityFile
    }

    private enum CodingKeys: String, CodingKey {
        case id, name, sshHost, sshPort, user, remoteHost, remotePort, localPort, authMode, identityFile
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = try container.decodeIfPresent(String.self, forKey: .id) ?? UUID().uuidString
        name = try container.decodeIfPresent(String.self, forKey: .name) ?? ""
        sshHost = try container.decodeIfPresent(String.self, forKey: .sshHost) ?? ""
        sshPort = try container.decodeIfPresent(Int.self, forKey: .sshPort) ?? 22
        user = try container.decodeIfPresent(String.self, forKey: .user) ?? ""
        remoteHost = try container.decodeIfPresent(String.self, forKey: .remoteHost) ?? "127.0.0.1"
        remotePort = try container.decodeIfPresent(Int.self, forKey: .remotePort) ?? 3080
        localPort = try container.decodeIfPresent(Int.self, forKey: .localPort) ?? 3080
        authMode = try container.decodeIfPresent(String.self, forKey: .authMode) ?? TunnelProfile.authModeNssm
        identityFile = try container.decodeIfPresent(String.self, forKey: .identityFile) ?? ""
    }

    var displayName: String {
        name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? sshHost : name
    }

    var summary: String {
        "\(user)@\(sshHost):\(sshPort) -> \(remoteHost):\(remotePort)"
    }

    var isValid: Bool {
        !sshHost.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty &&
        !user.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty &&
        !remoteHost.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty &&
        (1...65535).contains(sshPort) &&
        (1...65535).contains(remotePort) &&
        (1...65535).contains(localPort)
    }
}

struct ProfileConfig: Codable {
    var version: Int
    var tunnels: [TunnelProfile]
    var directs: [String]

    init(version: Int = 1, tunnels: [TunnelProfile] = [], directs: [String] = []) {
        self.version = version
        self.tunnels = tunnels
        self.directs = directs
    }

    private enum CodingKeys: String, CodingKey { case version, tunnels, directs }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        version = try container.decodeIfPresent(Int.self, forKey: .version) ?? 1
        tunnels = try container.decodeIfPresent([TunnelProfile].self, forKey: .tunnels) ?? []
        directs = try container.decodeIfPresent([String].self, forKey: .directs) ?? []
    }
}
