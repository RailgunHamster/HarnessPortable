import Foundation

struct AppSettings: Codable {
    static let defaultGitee = "https://gitee.com/railgunhamster/HarnessPortable/raw/updates"
    var version: Int = 1
    var closeBehavior: String = "exit"
    var restoreLastLayoutOnStartup: Bool = true
    var lastLayoutName: String = ""
    var reconnectRestoredWorkspace: Bool = true
    var startupReconnectPreference: Bool?
    var connectionSidebarCollapsed: Bool = false
    var appearance: String = "system"
    var connections: [String: ConnectionPreference] = [:]
    var connectionOrder: [String] = []
    /// Update feed slot: LAN share the Mac reaches through a mounted SMB
    /// volume (or any readable path/URL). Default mirrors the Windows home
    /// share; the smb:// form is mapped onto its /Volumes mount at use time.
    var updateHomeUrl: String = "smb://server-home/public/Software/HarnessPortable-Releases"
    /// Update feed slot: GitHub release download base; macos.json is fetched
    /// from `<base>/macos.json` and the dmg from `<base>/<name-in-feed>`.
    var updateGithubUrl: String = "https://github.com/RailgunHamster/HarnessPortable/releases/latest/download"
    /// `home` / `github`; both slots are always kept so switching never loses
    /// the other address.
    var updateSourceSelected: String = "gitee"
    var updateGiteeUrl: String = AppSettings.defaultGitee
    var updateProxyEnabled: Bool = false
    var updateProxyAddress: String = "localhost:7890"
    /// Per-direct-URL auth mode (macOS only; tunnels carry their own field).
    /// `none` (default) or `launchd` = read this Mac's dsh-web Launch Agent
    /// log and append its token to the URL. Keyed by the direct URL string.
    var directAuthModes: [String: String] = [:]

    private enum CodingKeys: String, CodingKey {
        case version, closeBehavior, restoreLastLayoutOnStartup, lastLayoutName
        case reconnectRestoredWorkspace, connectionSidebarCollapsed, appearance, connections
        case startupReconnectPreference, connectionOrder
        case updateHomeUrl, updateGithubUrl, updateGiteeUrl, updateSourceSelected
        case updateProxyEnabled, updateProxyAddress
        case directAuthModes
        case legacyUpdateServerUrl = "updateServerUrl"
    }

    init() {}

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        version = try container.decodeIfPresent(Int.self, forKey: .version) ?? 1
        closeBehavior = try container.decodeIfPresent(String.self, forKey: .closeBehavior) ?? "exit"
        restoreLastLayoutOnStartup = try container.decodeIfPresent(Bool.self, forKey: .restoreLastLayoutOnStartup) ?? true
        lastLayoutName = try container.decodeIfPresent(String.self, forKey: .lastLayoutName) ?? ""
        startupReconnectPreference = try container.decodeIfPresent(Bool.self, forKey: .startupReconnectPreference)
        reconnectRestoredWorkspace = startupReconnectPreference ?? true
        connectionOrder = try container.decodeIfPresent([String].self, forKey: .connectionOrder) ?? []
        connectionSidebarCollapsed = try container.decodeIfPresent(Bool.self, forKey: .connectionSidebarCollapsed) ?? false
        appearance = try container.decodeIfPresent(String.self, forKey: .appearance) ?? "system"
        connections = try container.decodeIfPresent([String: ConnectionPreference].self, forKey: .connections) ?? [:]
        updateHomeUrl = try container.decodeIfPresent(String.self, forKey: .updateHomeUrl)
            ?? "smb://server-home/public/Software/HarnessPortable-Releases"
        updateGithubUrl = try container.decodeIfPresent(String.self, forKey: .updateGithubUrl)
            ?? "https://github.com/RailgunHamster/HarnessPortable/releases/latest/download"
        let legacy = try container.decodeIfPresent(String.self, forKey: .legacyUpdateServerUrl)
        // The pre-slot field is folded into the matching slot instead of being
        // lost on first upgrade (same rule as the Windows/Android ports).
        if let legacy, !legacy.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            let trimmed = legacy.trimmingCharacters(in: .whitespacesAndNewlines)
            if trimmed.lowercased().hasPrefix("smb://") || trimmed.hasPrefix("/") {
                updateHomeUrl = trimmed
            } else {
                updateGithubUrl = trimmed
            }
        }
        updateSourceSelected = try container.decodeIfPresent(String.self, forKey: .updateSourceSelected)
            ?? ((legacy?.lowercased().hasPrefix("smb://") == true || legacy?.hasPrefix("/") == true) ? "home" : "github")
        let hadGitee = container.contains(.updateGiteeUrl)
        updateGiteeUrl = try container.decodeIfPresent(String.self, forKey: .updateGiteeUrl) ?? Self.defaultGitee
        if !hadGitee && updateSourceSelected == "github" && updateGithubUrl.lowercased().hasPrefix("https://github.com/railgunhamster/harnessportable/") {
            updateSourceSelected = "gitee"
        }
        if !["github", "home", "gitee"].contains(updateSourceSelected) { updateSourceSelected = "gitee" }
        updateProxyEnabled = try container.decodeIfPresent(Bool.self, forKey: .updateProxyEnabled) ?? false
        updateProxyAddress = try container.decodeIfPresent(String.self, forKey: .updateProxyAddress) ?? "localhost:7890"
        directAuthModes = try container.decodeIfPresent([String: String].self, forKey: .directAuthModes) ?? [:]
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        // The legacy mirror field is decode-only; never write it back.
        try container.encode(version, forKey: .version)
        try container.encode(closeBehavior, forKey: .closeBehavior)
        try container.encode(restoreLastLayoutOnStartup, forKey: .restoreLastLayoutOnStartup)
        try container.encode(lastLayoutName, forKey: .lastLayoutName)
        try container.encode(reconnectRestoredWorkspace, forKey: .reconnectRestoredWorkspace)
        try container.encodeIfPresent(startupReconnectPreference, forKey: .startupReconnectPreference)
        try container.encode(connectionOrder, forKey: .connectionOrder)
        try container.encode(connectionSidebarCollapsed, forKey: .connectionSidebarCollapsed)
        try container.encode(appearance, forKey: .appearance)
        try container.encode(connections, forKey: .connections)
        try container.encode(updateHomeUrl, forKey: .updateHomeUrl)
        try container.encode(updateGithubUrl, forKey: .updateGithubUrl)
        try container.encode(updateGiteeUrl, forKey: .updateGiteeUrl)
        try container.encode(updateSourceSelected, forKey: .updateSourceSelected)
        try container.encode(updateProxyEnabled, forKey: .updateProxyEnabled)
        try container.encode(updateProxyAddress, forKey: .updateProxyAddress)
        if !directAuthModes.isEmpty {
            try container.encode(directAuthModes, forKey: .directAuthModes)
        }
    }
}

struct ConnectionPreference: Codable {
    var favorite: Bool = false
    var group: String = ""
    var name: String = ""
    var lastOpened: String = ""
    init() {}
    private enum CodingKeys: String, CodingKey { case favorite, group, name, lastOpened }
    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        favorite = try c.decodeIfPresent(Bool.self, forKey: .favorite) ?? false
        group = try c.decodeIfPresent(String.self, forKey: .group) ?? ""
        name = try c.decodeIfPresent(String.self, forKey: .name) ?? ""
        lastOpened = try c.decodeIfPresent(String.self, forKey: .lastOpened) ?? ""
    }
}
