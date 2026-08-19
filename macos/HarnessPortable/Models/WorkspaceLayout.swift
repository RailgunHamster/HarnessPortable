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

struct WorkspaceTab: Identifiable, Codable, Hashable {
    let id: UUID
    var kind: TabKind
    var profileID: String?
    var url: String?
    var label: String?

    init(
        id: UUID = UUID(),
        kind: TabKind,
        profileID: String? = nil,
        url: String? = nil,
        label: String? = nil
    ) {
        self.id = id
        self.kind = kind
        self.profileID = profileID
        self.url = url
        self.label = label
    }

    private enum CodingKeys: String, CodingKey { case kind, profileID, url, label }

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        id = UUID()
        kind = try container.decode(TabKind.self, forKey: .kind)
        profileID = try container.decodeIfPresent(String.self, forKey: .profileID)
        url = try container.decodeIfPresent(String.self, forKey: .url)
        label = try container.decodeIfPresent(String.self, forKey: .label)
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
