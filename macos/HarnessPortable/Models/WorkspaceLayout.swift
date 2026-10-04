import Foundation

enum TabKind: String, Codable, Hashable {
    case management
    case tunnel
    case direct
}

enum LayoutNodeKind: String, Codable, Hashable {
    case pane
    case split
}

enum SplitOrientation: String, Codable, Hashable {
    case horizontal
    case vertical
}

/// One tab's captured dsh web view state, mirrored into the page URL by the
/// `dsh-view-state` dsh plugin. Only the plugin's three whitelisted parameters
/// are ever read into it — never the page URL itself, which carries dsh's
/// process token on first navigation. Null fields mean "not recorded".
struct LayoutWebState: Codable, Hashable {
    /// Selected session id, absent when there is none.
    var sessionId: String?
    /// Sidebar width in px; 0 means collapsed.
    var sidebar: Int?
    /// Right panel's saved width in px; 0 means no width was recorded yet.
    var rightbar: Int?

    init(sessionId: String? = nil, sidebar: Int? = nil, rightbar: Int? = nil) {
        self.sessionId = sessionId
        self.sidebar = sidebar
        self.rightbar = rightbar
    }

    private enum CodingKeys: String, CodingKey {
        case sessionId
        case sidebar
        case rightbar
        // Windows presets serialize PascalCase members; accept them on load so a
        // preset file copied between platforms still restores its web state.
        case pascalSessionId = "SessionId"
        case pascalSidebar = "Sidebar"
        case pascalRightbar = "Rightbar"
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        sessionId = try container.decodeIfPresent(String.self, forKey: .sessionId)
            ?? container.decodeIfPresent(String.self, forKey: .pascalSessionId)
        sidebar = try container.decodeIfPresent(Int.self, forKey: .sidebar)
            ?? container.decodeIfPresent(Int.self, forKey: .pascalSidebar)
        rightbar = try container.decodeIfPresent(Int.self, forKey: .rightbar)
            ?? container.decodeIfPresent(Int.self, forKey: .pascalRightbar)
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        // Always write the macOS (camelCase) shape; decoding accepts both.
        try container.encodeIfPresent(sessionId, forKey: .sessionId)
        try container.encodeIfPresent(sidebar, forKey: .sidebar)
        try container.encodeIfPresent(rightbar, forKey: .rightbar)
    }
}

struct WorkspaceTab: Identifiable, Codable, Hashable {
    let id: UUID
    var kind: TabKind
    var profileID: String?
    var url: String?
    var label: String?
    /// Per-tab dsh web view state captured when the layout was saved. Null when
    /// the `dsh-view-state` plugin is not installed or the page carried nothing.
    var web: LayoutWebState?

    init(
        id: UUID = UUID(),
        kind: TabKind,
        profileID: String? = nil,
        url: String? = nil,
        label: String? = nil,
        web: LayoutWebState? = nil
    ) {
        self.id = id
        self.kind = kind
        self.profileID = profileID
        self.url = url
        self.label = label
        self.web = web
    }

    private enum CodingKeys: String, CodingKey { case kind, profileID, url, label, web }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = UUID()
        kind = try container.decode(TabKind.self, forKey: .kind)
        profileID = try container.decodeIfPresent(String.self, forKey: .profileID)
        url = try container.decodeIfPresent(String.self, forKey: .url)
        label = try container.decodeIfPresent(String.self, forKey: .label)
        web = try container.decodeIfPresent(LayoutWebState.self, forKey: .web)
    }
}

struct LayoutNode: Identifiable, Codable, Hashable {
    let id: UUID
    var kind: LayoutNodeKind
    var orientation: SplitOrientation?
    var children: [LayoutNode]
    var tabs: [WorkspaceTab]
    var dockWidth: String?
    var dockHeight: String?

    init(
        id: UUID = UUID(),
        kind: LayoutNodeKind,
        orientation: SplitOrientation? = nil,
        children: [LayoutNode] = [],
        tabs: [WorkspaceTab] = [],
        dockWidth: String? = nil,
        dockHeight: String? = nil
    ) {
        self.id = id
        self.kind = kind
        self.orientation = orientation
        self.children = children
        self.tabs = tabs
        self.dockWidth = dockWidth
        self.dockHeight = dockHeight
    }

    static func pane(_ tabs: [WorkspaceTab] = []) -> LayoutNode {
        LayoutNode(kind: .pane, tabs: tabs)
    }

    static func split(_ orientation: SplitOrientation, _ children: [LayoutNode]) -> LayoutNode {
        LayoutNode(kind: .split, orientation: orientation, children: children, dockWidth: "1|Star", dockHeight: "1|Star")
    }

    private enum CodingKeys: String, CodingKey {
        case kind, orientation, children, tabs, dockWidth, dockHeight
    }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = UUID()
        kind = try container.decodeIfPresent(LayoutNodeKind.self, forKey: .kind) ?? .pane
        orientation = try container.decodeIfPresent(SplitOrientation.self, forKey: .orientation)
        children = try container.decodeIfPresent([LayoutNode].self, forKey: .children) ?? []
        tabs = try container.decodeIfPresent([WorkspaceTab].self, forKey: .tabs) ?? []
        dockWidth = try container.decodeIfPresent(String.self, forKey: .dockWidth)
        dockHeight = try container.decodeIfPresent(String.self, forKey: .dockHeight)
    }
}

struct WorkspaceLayout: Codable, Identifiable, Hashable {
    var id: String { name }
    var name: String
    var savedAt: String
    var root: LayoutNode

    init(name: String, savedAt: String = ISO8601DateFormatter().string(from: Date()), root: LayoutNode) {
        self.name = name
        self.savedAt = savedAt
        self.root = root
    }
}
