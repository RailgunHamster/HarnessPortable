import Foundation

struct AppSettings: Codable {
    static let defaultCnb = "https://cnb.cool/railgunhamster/HarnessPortable"
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
    var updateGithubUrl: String = "https://github.com/RailgunHamster/HarnessPortable/releases/latest/download"
    var updateSourceSelected: String = "cnb"
    var updateCnbUrl: String = AppSettings.defaultCnb
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
        case updateGithubUrl, updateCnbUrl, updateSourceSelected
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
        let legacy = try container.decodeIfPresent(String.self, forKey: .legacyUpdateServerUrl)
        updateGithubUrl = try container.decodeIfPresent(String.self, forKey: .updateGithubUrl)
            ?? ((URL(string: legacy ?? "")?.host == "github.com") ? legacy! : "https://github.com/RailgunHamster/HarnessPortable/releases/latest/download")
        let hadCnb = container.contains(.updateCnbUrl)
        updateCnbUrl = try container.decodeIfPresent(String.self, forKey: .updateCnbUrl) ?? Self.defaultCnb
        updateSourceSelected = try container.decodeIfPresent(String.self, forKey: .updateSourceSelected) ?? "cnb"
        if !["cnb", "github"].contains(updateSourceSelected) { updateSourceSelected = "cnb" }
        if !hadCnb && updateSourceSelected == "github" && updateGithubUrl.lowercased().hasPrefix("https://github.com/railgunhamster/harnessportable") {
            updateSourceSelected = "cnb"
        }
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
        try container.encode(updateGithubUrl, forKey: .updateGithubUrl)
        try container.encode(updateCnbUrl, forKey: .updateCnbUrl)
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
