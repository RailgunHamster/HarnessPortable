import Foundation
import Combine

enum SplitDirection: Equatable {
    case left
    case right
    case up
    case down

    var orientation: SplitOrientation {
        switch self {
        case .left, .right: return .horizontal
        case .up, .down: return .vertical
        }
    }

    var insertsBefore: Bool {
        switch self {
        case .left, .up: return true
        case .right, .down: return false
        }
    }
}

@MainActor
final class WorkspaceStore: ObservableObject {
    @Published private(set) var root: LayoutNode
    @Published private(set) var activePaneID: UUID
    @Published var renameTarget: UUID?
    @Published var renameText = ""

    @Published private var selectedTabIDs: [UUID: UUID] = [:]

    init() {
        let management = WorkspaceTab(kind: .management)
        let pane = LayoutNode.pane([management])
        root = pane
        activePaneID = pane.id
        selectedTabIDs[pane.id] = management.id
    }

    func node(_ id: UUID) -> LayoutNode? {
        findNode(root, id: id)
    }

    func tabs(in paneID: UUID) -> [WorkspaceTab] {
        node(paneID)?.tabs ?? []
    }

    func selectedTab(in paneID: UUID) -> WorkspaceTab? {
        let tabs = tabs(in: paneID)
        if let selected = selectedTabIDs[paneID], let tab = tabs.first(where: { $0.id == selected }) {
            return tab
        }
        return tabs.first
    }

    func selectPane(_ paneID: UUID) {
        guard node(paneID)?.kind == .pane else { return }
        activePaneID = paneID
        if selectedTabIDs[paneID] == nil, let first = tabs(in: paneID).first {
            selectedTabIDs[paneID] = first.id
        }
    }

    func selectTab(_ tabID: UUID, in paneID: UUID) {
        guard tabs(in: paneID).contains(where: { $0.id == tabID }) else { return }
        activePaneID = paneID
        selectedTabIDs[paneID] = tabID
    }

    func openManagement() {
        guard let location = findTab(root, tabID: nil, kind: .management) else { return }
        selectTab(location.tab.id, in: location.paneID)
    }

    func openDirect(_ url: String) {
        let tab = WorkspaceTab(kind: .direct, url: url)
        append(tab, to: activePaneID)
    }

    func ensureTunnelTab(profileID: String, focus: Bool = true) {
        if let existing = findTab(root, kind: .tunnel, profileID: profileID) {
            if focus { selectTab(existing.tab.id, in: existing.paneID) }
            return
        }
        append(WorkspaceTab(kind: .tunnel, profileID: profileID), to: activePaneID)
    }

    func openTunnelTab(profileID: String) {
        append(WorkspaceTab(kind: .tunnel, profileID: profileID), to: activePaneID)
    }

    @discardableResult
    func closeTunnelTabs(profileID: String) -> [UUID] {
        let ids = allTabs().filter { $0.tab.kind == .tunnel && $0.tab.profileID == profileID }.map(\.tab.id)
        for id in ids { closeTab(id) }
        return ids
    }

    func requestRename(_ tab: WorkspaceTab) {
        guard tab.kind != .management else { return }
        renameTarget = tab.id
        renameText = tab.label ?? ""
    }

    func commitRename() {
        guard let target = renameTarget else { return }
        let value = renameText.trimmingCharacters(in: .whitespacesAndNewlines)
        updateTab(target) { tab in
            tab.label = value.isEmpty ? nil : value
        }
        renameTarget = nil
    }

    func cancelRename() {
        renameTarget = nil
    }

    func duplicateTab(_ tabID: UUID, to paneID: UUID? = nil) {
        guard let source = findTab(root, tabID: tabID)?.tab, source.kind != .management else { return }
        let duplicate = WorkspaceTab(kind: source.kind, profileID: source.profileID, url: source.url, label: source.label)
        append(duplicate, to: paneID ?? activePaneID)
    }

    func closeTab(_ tabID: UUID) {
        guard let source = findTab(root, tabID: tabID), source.tab.kind != .management else { return }
        guard removeTab(&root, tabID: tabID) != nil else { return }
        prune(&root)
        selectedTabIDs = selectedTabIDs.filter { node($0.key)?.kind == .pane }
        if node(activePaneID) == nil {
            activePaneID = firstPaneID(root) ?? activePaneID
        }
        if let pane = node(activePaneID), let first = pane.tabs.first, selectedTabIDs[activePaneID] == nil {
            selectedTabIDs[activePaneID] = first.id
        }
    }

    func replaceTab(_ tabID: UUID, with replacement: WorkspaceTab) {
        updateTab(tabID) { current in
            current.kind = replacement.kind
            current.profileID = replacement.profileID
            current.url = replacement.url
            current.label = replacement.label
        }
    }

    func moveTab(_ tabID: UUID, to paneID: UUID) {
        guard let source = findTab(root, tabID: tabID), node(paneID)?.kind == .pane else { return }
        if source.paneID == paneID { return }
        guard let tab = removeTab(&root, tabID: tabID) else { return }
        append(tab, to: paneID)
        activePaneID = paneID
        selectedTabIDs[paneID] = tab.id
    }

    @discardableResult
    func splitPane(_ paneID: UUID, direction: SplitDirection, duplicateTabID: UUID? = nil) -> UUID? {
        guard node(paneID)?.kind == .pane else { return nil }
        let newPaneID = UUID()
        var newPane = LayoutNode(id: newPaneID, kind: .pane)
        if let duplicateTabID, let source = findTab(root, tabID: duplicateTabID)?.tab {
            newPane.tabs = [WorkspaceTab(kind: source.kind, profileID: source.profileID, url: source.url, label: source.label)]
        }

        var updated = root
        let didReplace = replaceNode(&updated, id: paneID) { old in
            let first = direction.insertsBefore ? newPane : old
            let second = direction.insertsBefore ? old : newPane
            return LayoutNode.split(direction.orientation, [first, second])
        }
        guard didReplace else { return nil }

        root = updated
        if let first = newPane.tabs.first { selectedTabIDs[newPaneID] = first.id }
        if duplicateTabID != nil { activePaneID = newPaneID }
        return newPaneID
    }

    @discardableResult
    func moveTabToSplit(_ tabID: UUID, paneID: UUID, direction: SplitDirection) -> UUID? {
        guard let source = findTab(root, tabID: tabID)?.tab else { return nil }
        guard let newPaneID = splitPane(paneID, direction: direction) else { return nil }
        guard let moved = removeTab(&root, tabID: source.id) else { return nil }
        append(moved, to: newPaneID)
        activePaneID = newPaneID
        selectedTabIDs[newPaneID] = moved.id
        return newPaneID
    }

    func setSplitRatio(_ nodeID: UUID, ratio: Double) {
        let clamped = min(max(ratio, 0.15), 0.85)
        updateNode(&root, id: nodeID) { node in
            if node.orientation == .vertical {
                node.dockHeight = "\(clamped)|Star"
            } else {
                node.dockWidth = "\(clamped)|Star"
            }
        }
    }

    func splitRatio(for node: LayoutNode) -> Double {
        let raw = node.orientation == .vertical ? node.dockHeight : node.dockWidth
        guard let raw else { return 0.5 }
        let parts = raw.split(separator: "|")
        guard parts.count == 2, parts[1].caseInsensitiveCompare("Star") == .orderedSame,
              let value = Double(parts[0]), value > 0, value < 1 else { return 0.5 }
        return value
    }

    func allTabs() -> [(paneID: UUID, tab: WorkspaceTab)] {
        var result: [(UUID, WorkspaceTab)] = []
        collectTabs(root, into: &result)
        return result
    }

    func captureLayout() -> LayoutNode {
        root
    }

    func restore(
        _ layout: WorkspaceLayout,
        validProfileIDs: Set<String>? = nil,
        validDirectURLs: Set<String>? = nil
    ) {
        var restored = layout.root
        filterInvalidTabs(&restored, validProfileIDs: validProfileIDs, validDirectURLs: validDirectURLs)
        prune(&restored)
        if !containsManagement(restored) {
            if let paneID = firstPaneID(restored) {
                _ = append(WorkspaceTab(kind: .management), to: paneID, root: &restored)
            } else {
                restored = .pane([WorkspaceTab(kind: .management)])
            }
        }
        root = restored
        selectedTabIDs = [:]
        initializeSelections(root)
        activePaneID = firstPaneID(root) ?? root.id
    }

    private func append(_ tab: WorkspaceTab, to paneID: UUID) {
        _ = append(tab, to: paneID, root: &root)
        activePaneID = paneID
        selectedTabIDs[paneID] = tab.id
    }

    @discardableResult
    private func append(_ tab: WorkspaceTab, to paneID: UUID, root: inout LayoutNode) -> Bool {
        return updateNode(&root, id: paneID) { node in
            guard node.kind == .pane else { return }
            node.tabs.append(tab)
        }
    }

    private func updateTab(_ tabID: UUID, _ body: (inout WorkspaceTab) -> Void) {
        updateNode(&root, id: findTab(root, tabID: tabID)?.paneID ?? UUID()) { node in
            guard let index = node.tabs.firstIndex(where: { $0.id == tabID }) else { return }
            body(&node.tabs[index])
        }
    }

    @discardableResult
    private func updateNode(_ node: inout LayoutNode, id: UUID, body: (inout LayoutNode) -> Void) -> Bool {
        if node.id == id {
            body(&node)
            return true
        }
        for index in node.children.indices {
            if updateNode(&node.children[index], id: id, body: body) { return true }
        }
        return false
    }

    @discardableResult
    private func replaceNode(_ node: inout LayoutNode, id: UUID, replacement: (LayoutNode) -> LayoutNode) -> Bool {
        if node.id == id {
            node = replacement(node)
            return true
        }
        for index in node.children.indices {
            if replaceNode(&node.children[index], id: id, replacement: replacement) { return true }
        }
        return false
    }

    private func findNode(_ node: LayoutNode, id: UUID) -> LayoutNode? {
        if node.id == id { return node }
        for child in node.children {
            if let found = findNode(child, id: id) { return found }
        }
        return nil
    }

    private func findTab(_ node: LayoutNode, tabID: UUID? = nil, kind: TabKind? = nil, profileID: String? = nil) -> (paneID: UUID, tab: WorkspaceTab)? {
        if node.kind == .pane {
            for tab in node.tabs {
                let idMatches = tabID == nil || tab.id == tabID
                let kindMatches = kind == nil || tab.kind == kind
                let profileMatches = profileID == nil || tab.profileID == profileID
                if idMatches && kindMatches && profileMatches { return (node.id, tab) }
            }
        }
        for child in node.children {
            if let found = findTab(child, tabID: tabID, kind: kind, profileID: profileID) { return found }
        }
        return nil
    }

    @discardableResult
    private func removeTab(_ node: inout LayoutNode, tabID: UUID) -> WorkspaceTab? {
        if let index = node.tabs.firstIndex(where: { $0.id == tabID }) {
            return node.tabs.remove(at: index)
        }
        for index in node.children.indices {
            if let removed = removeTab(&node.children[index], tabID: tabID) { return removed }
        }
        return nil
    }

    private func filterInvalidTabs(
        _ node: inout LayoutNode,
        validProfileIDs: Set<String>?,
        validDirectURLs: Set<String>?
    ) {
        if node.kind == .pane {
            node.tabs.removeAll { tab in
                switch tab.kind {
                case .management:
                    return false
                case .tunnel:
                    guard let validProfileIDs else { return false }
                    return tab.profileID == nil || !validProfileIDs.contains(tab.profileID!)
                case .direct:
                    guard let validDirectURLs else { return false }
                    return tab.url == nil || !validDirectURLs.contains(tab.url!)
                }
            }
        }
        for index in node.children.indices {
            filterInvalidTabs(&node.children[index], validProfileIDs: validProfileIDs, validDirectURLs: validDirectURLs)
        }
    }

    private func prune(_ node: inout LayoutNode) {
        for index in node.children.indices { prune(&node.children[index]) }
        node.children.removeAll { child in
            child.kind == .pane ? child.tabs.isEmpty : child.children.isEmpty
        }
        if node.kind == .split, node.children.count == 1 {
            node = node.children[0]
        }
    }

    private func containsManagement(_ node: LayoutNode) -> Bool {
        if node.kind == .pane { return node.tabs.contains { $0.kind == .management } }
        return node.children.contains(where: containsManagement)
    }

    private func firstPaneID(_ node: LayoutNode) -> UUID? {
        if node.kind == .pane { return node.id }
        return node.children.compactMap(firstPaneID).first
    }

    private func collectTabs(_ node: LayoutNode, into result: inout [(UUID, WorkspaceTab)]) {
        if node.kind == .pane { result.append(contentsOf: node.tabs.map { (node.id, $0) }) }
        for child in node.children { collectTabs(child, into: &result) }
    }

    private func initializeSelections(_ node: LayoutNode) {
        if node.kind == .pane, let first = node.tabs.first { selectedTabIDs[node.id] = first.id }
        for child in node.children { initializeSelections(child) }
    }
}
