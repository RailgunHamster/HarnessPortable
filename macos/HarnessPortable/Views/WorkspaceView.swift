import SwiftUI
import AppKit
import UniformTypeIdentifiers

@MainActor
struct WorkspaceView: View {
    @ObservedObject var services: AppServices
    @StateObject private var workspace: WorkspaceStore
    @StateObject private var webSessions = WebSessionStore()

    @State private var isFullScreen = false
    @State private var passwordProfile: TunnelProfile?
    @State private var keychainError: String?
    @State private var directPromptPresented = false
    @State private var directURLInput = ""
    @State private var saveLayoutPresented = false
    @State private var layoutName = ""
    @State private var selectedLayoutName: String?
    @State private var keyMonitor: Any?
    @State private var didStart = false

    init(services: AppServices) {
        self.services = services
        _workspace = StateObject(wrappedValue: WorkspaceStore())
    }

    var body: some View {
        VStack(spacing: 0) {
            if !isFullScreen {
                toolbar
                Divider()
            }

            WorkspaceNodeView(
                workspace: workspace,
                nodeID: workspace.root.id,
                services: services,
                webSessions: webSessions,
                onConnect: connect,
                onOpenTunnel: openTunnel,
                onStop: stop,
                onOpenDirect: openDirect,
                onSwitch: switchTab,
                onDelete: deleteProfile
            )
            .frame(maxWidth: .infinity, maxHeight: .infinity)

            if !isFullScreen {
                Divider()
                statusBar
            }
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .frame(minWidth: 900, minHeight: 600)
        .onAppear {
            startWorkspaceIfNeeded()
            installKeyMonitor()
        }
        .onDisappear { removeKeyMonitor() }
        .onReceive(services.tunnels.$states) { states in
            handleTunnelStates(states)
        }
        .sheet(item: $passwordProfile) { profile in
            PasswordPromptView(profile: profile) { password in
                do {
                    try services.keychain.setPassword(password, for: profile.id)
                    connect(profile)
                    return true
                } catch {
                    keychainError = error.localizedDescription
                    return false
                }
            }
        }
        .alert("保存布局", isPresented: $saveLayoutPresented) {
            TextField("布局名称", text: $layoutName)
            Button("取消", role: .cancel) {}
            Button("保存") { saveLayout() }
        }
        .alert("新建直连", isPresented: $directPromptPresented) {
            TextField("主机或 URL", text: $directURLInput)
            Button("取消", role: .cancel) {}
            Button("打开") { openDirectInput() }
        }
        .alert("重命名标签", isPresented: renameAlertPresented) {
            TextField("标签名称", text: $workspace.renameText)
            Button("取消", role: .cancel) { workspace.cancelRename() }
            Button("保存") { workspace.commitRename() }
        }
        .alert("无法保存 SSH 密码", isPresented: keychainErrorPresented) {
            Button("确定", role: .cancel) { keychainError = nil }
        } message: {
            Text(keychainError ?? "未知 Keychain 错误")
        }
    }

    private var toolbar: some View {
        HStack(spacing: 10) {
            Text("Harness Portable")
                .font(.headline)
            Divider().frame(height: 18)
            Button {
                workspace.openManagement()
            } label: {
                Label("管理", systemImage: "slider.horizontal.3")
            }
            Button {
                directURLInput = ""
                directPromptPresented = true
            } label: {
                Label("新建直连", systemImage: "plus")
            }
            Divider().frame(height: 18)
            Picker("布局", selection: layoutSelection) {
                Text("当前布局").tag(nil as String?)
                ForEach(services.layouts.layouts) { layout in
                    Text(layout.name).tag(Optional(layout.name))
                }
            }
            .labelsHidden()
            .frame(width: 170)
            Button {
                layoutName = selectedLayoutName ?? ""
                saveLayoutPresented = true
            } label: {
                Image(systemName: "square.and.arrow.down")
            }
            .help("保存布局")
            if selectedLayoutName != nil {
                Button {
                    deleteSelectedLayout()
                } label: {
                    Image(systemName: "trash")
                }
                .help("删除布局")
            }
            Spacer()
            Button {
                toggleFullScreen()
            } label: {
                Image(systemName: isFullScreen ? "arrow.down.right.and.arrow.up.left" : "arrow.up.left.and.arrow.down.right")
            }
            .help(isFullScreen ? "退出全屏" : "全屏")
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 9)
    }

    private var statusBar: some View {
        HStack(spacing: 12) {
            if services.tunnels.activeStates.isEmpty {
                Label("就绪", systemImage: "circle")
                    .foregroundStyle(.secondary)
            } else {
                ForEach(services.tunnels.activeStates) { state in
                    Label(statusLabel(state), systemImage: statusIcon(state.status))
                        .foregroundStyle(state.status == .connected ? Color.green : Color.orange)
                        .lineLimit(1)
                }
            }
            Spacer()
            Text("\(workspace.allTabs().count) 个标签")
                .foregroundStyle(.secondary)
        }
        .font(.caption)
        .padding(.horizontal, 14)
        .padding(.vertical, 6)
    }

    private var layoutSelection: Binding<String?> {
        Binding(
            get: { selectedLayoutName },
            set: { name in
                selectedLayoutName = name
                guard let name, let layout = services.layouts.named(name) else { return }
                apply(layout)
            }
        )
    }

    private var renameAlertPresented: Binding<Bool> {
        Binding(
            get: { workspace.renameTarget != nil },
            set: { presented in if !presented { workspace.cancelRename() } }
        )
    }

    private var keychainErrorPresented: Binding<Bool> {
        Binding(
            get: { keychainError != nil },
            set: { isPresented in
                if !isPresented { keychainError = nil }
            }
        )
    }

    private func startWorkspaceIfNeeded() {
        guard !didStart else { return }
        didStart = true
        if services.settings.value.restoreLastLayoutOnStartup,
           !services.settings.value.lastLayoutName.isEmpty,
           let layout = services.layouts.named(services.settings.value.lastLayoutName) {
            selectedLayoutName = layout.name
            workspace.restore(
                layout,
                validProfileIDs: Set(services.profiles.tunnels.map(\.id)),
                validDirectURLs: Set(services.profiles.directs)
            )
            startTunnelsInWorkspace()
        }
    }

    private func startTunnelsInWorkspace() {
        for item in workspace.allTabs() where item.tab.kind == .tunnel {
            guard let profile = services.profiles.findTunnel(id: item.tab.profileID),
                  services.keychain.hasPassword(for: profile.id) else { continue }
            services.tunnels.start(profile)
        }
    }

    private func handleTunnelStates(_ states: [String: TunnelInfo]) {
        for state in states.values {
            guard let profileID = state.profileID else { continue }
            switch state.status {
            case .connected:
                workspace.ensureTunnelTab(profileID: profileID, focus: false)
            case .stopped:
                let closed = workspace.closeTunnelTabs(profileID: profileID)
                closed.forEach { webSessions.remove(tabID: $0) }
            case .idle, .connecting, .retrying, .failed:
                break
            }
        }
    }

    private func connect(_ profile: TunnelProfile) {
        if services.keychain.hasPassword(for: profile.id) {
            services.tunnels.start(profile)
            workspace.ensureTunnelTab(profileID: profile.id)
        } else {
            passwordProfile = profile
        }
    }

    private func openTunnel(_ profile: TunnelProfile) {
        if services.keychain.hasPassword(for: profile.id) {
            services.tunnels.start(profile)
            workspace.openTunnelTab(profileID: profile.id)
        } else {
            passwordProfile = profile
        }
    }

    private func stop(_ profileID: String) {
        services.tunnels.stop(profileID)
        let closed = workspace.closeTunnelTabs(profileID: profileID)
        closed.forEach { webSessions.remove(tabID: $0) }
    }

    private func deleteProfile(_ profile: TunnelProfile) {
        stop(profile.id)
        services.keychain.deletePassword(for: profile.id)
        services.profiles.deleteTunnel(id: profile.id)
    }

    private func openDirect(_ url: String) {
        workspace.openDirect(url)
    }

    private func openDirectInput() {
        guard let url = services.profiles.addDirect(directURLInput) else { return }
        openDirect(url)
    }

    private func switchTab(_ tabID: UUID, target: WorkspaceTab) {
        if target.kind == .tunnel, let profile = services.profiles.findTunnel(id: target.profileID) {
            if services.keychain.hasPassword(for: profile.id) {
                workspace.replaceTab(tabID, with: target)
                webSessions.remove(tabID: tabID)
                services.tunnels.start(profile)
            } else {
                passwordProfile = profile
            }
        } else {
            workspace.replaceTab(tabID, with: target)
            webSessions.remove(tabID: tabID)
        }
    }

    private func saveLayout() {
        let name = layoutName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else { return }
        let layout = WorkspaceLayout(name: name, root: workspace.captureLayout())
        services.layouts.upsert(layout)
        selectedLayoutName = name
        services.settings.update { $0.lastLayoutName = name }
    }

    private func apply(_ layout: WorkspaceLayout) {
        workspace.restore(
            layout,
            validProfileIDs: Set(services.profiles.tunnels.map(\.id)),
            validDirectURLs: Set(services.profiles.directs)
        )
        services.settings.update { $0.lastLayoutName = layout.name }
        startTunnelsInWorkspace()
    }

    private func deleteSelectedLayout() {
        guard let name = selectedLayoutName else { return }
        services.layouts.delete(name: name)
        selectedLayoutName = nil
    }

    private func toggleFullScreen() {
        isFullScreen.toggle()
        NSApp.keyWindow?.toggleFullScreen(nil)
    }

    private func installKeyMonitor() {
        guard keyMonitor == nil else { return }
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { event in
            if event.keyCode == 103 {
                toggleFullScreen()
                return nil
            }
            if isFullScreen && event.keyCode == 53 {
                toggleFullScreen()
                return nil
            }
            return event
        }
    }

    private func removeKeyMonitor() {
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        keyMonitor = nil
    }

    private func statusLabel(_ state: TunnelInfo) -> String {
        let name = state.profileName ?? "隧道"
        if state.status == .connected, state.localPort > 0 { return "\(name) \(state.localPort)" }
        return name
    }

    private func statusIcon(_ status: TunnelStatus) -> String {
        switch status {
        case .connected: return "checkmark.circle.fill"
        case .connecting: return "ellipsis.circle"
        case .retrying: return "arrow.triangle.2.circlepath"
        case .failed: return "xmark.circle.fill"
        case .idle, .stopped: return "circle"
        }
    }
}

@MainActor
private struct WorkspaceNodeView: View {
    @ObservedObject var workspace: WorkspaceStore
    let nodeID: UUID
    @ObservedObject var services: AppServices
    @ObservedObject var webSessions: WebSessionStore
    let onConnect: (TunnelProfile) -> Void
    let onOpenTunnel: (TunnelProfile) -> Void
    let onStop: (String) -> Void
    let onOpenDirect: (String) -> Void
    let onSwitch: (UUID, WorkspaceTab) -> Void
    let onDelete: (TunnelProfile) -> Void
    var body: some View {
        if let node = workspace.node(nodeID) {
            if node.kind == .split, node.children.count >= 2 {
                ResizableWorkspaceSplit(
                    workspace: workspace,
                    node: node,
                    first: node.children[0],
                    second: node.children[1],
                    services: services,
                    webSessions: webSessions,
                    onConnect: onConnect,
                     onOpenTunnel: onOpenTunnel,
                    onStop: onStop,
                    onOpenDirect: onOpenDirect,
                    onSwitch: onSwitch,
                    onDelete: onDelete
                )
            } else {
                WorkspacePaneView(
                    workspace: workspace,
                    paneID: node.id,
                    services: services,
                    webSessions: webSessions,
                    onConnect: onConnect,
                     onOpenTunnel: onOpenTunnel,
                    onStop: onStop,
                    onOpenDirect: onOpenDirect,
                    onSwitch: onSwitch,
                    onDelete: onDelete
                )
            }
        } else {
            Color.clear
        }
    }
}

@MainActor
private struct ResizableWorkspaceSplit: View {
    @ObservedObject var workspace: WorkspaceStore
    let node: LayoutNode
    let first: LayoutNode
    let second: LayoutNode
    @ObservedObject var services: AppServices
    @ObservedObject var webSessions: WebSessionStore
    let onConnect: (TunnelProfile) -> Void
    let onOpenTunnel: (TunnelProfile) -> Void
    let onStop: (String) -> Void
    let onOpenDirect: (String) -> Void
    let onSwitch: (UUID, WorkspaceTab) -> Void
    let onDelete: (TunnelProfile) -> Void
    @State private var dragStartRatio: Double?

    var body: some View {
        GeometryReader { geometry in
            let ratio = workspace.splitRatio(for: node)
            if node.orientation == .vertical {
                VStack(spacing: 0) {
                    childView(first)
                        .frame(height: max(80, geometry.size.height * ratio - 2))
                    Divider()
                        .frame(height: 5)
                        .contentShape(Rectangle())
                        .gesture(verticalDrag(geometry: geometry, ratio: ratio))
                    childView(second)
                        .frame(height: max(80, geometry.size.height * (1 - ratio) - 2))
                }
            } else {
                HStack(spacing: 0) {
                    childView(first)
                        .frame(width: max(120, geometry.size.width * ratio - 2))
                    Divider()
                        .frame(width: 5)
                        .contentShape(Rectangle())
                        .gesture(horizontalDrag(geometry: geometry, ratio: ratio))
                    childView(second)
                        .frame(width: max(120, geometry.size.width * (1 - ratio) - 2))
                }
            }
        }
    }

    private func childView(_ child: LayoutNode) -> some View {
        WorkspaceNodeView(
            workspace: workspace,
            nodeID: child.id,
            services: services,
            webSessions: webSessions,
            onConnect: onConnect,
                     onOpenTunnel: onOpenTunnel,
            onStop: onStop,
            onOpenDirect: onOpenDirect,
            onSwitch: onSwitch,
            onDelete: onDelete
        )
    }

    private func horizontalDrag(geometry: GeometryProxy, ratio: Double) -> some Gesture {
        DragGesture(minimumDistance: 0)
            .onChanged { value in
                if dragStartRatio == nil { dragStartRatio = ratio }
                let base = dragStartRatio ?? ratio
                workspace.setSplitRatio(node.id, ratio: base + value.translation.width / max(1, geometry.size.width))
            }
            .onEnded { _ in dragStartRatio = nil }
    }

    private func verticalDrag(geometry: GeometryProxy, ratio: Double) -> some Gesture {
        DragGesture(minimumDistance: 0)
            .onChanged { value in
                if dragStartRatio == nil { dragStartRatio = ratio }
                let base = dragStartRatio ?? ratio
                workspace.setSplitRatio(node.id, ratio: base + value.translation.height / max(1, geometry.size.height))
            }
            .onEnded { _ in dragStartRatio = nil }
    }
}

@MainActor
private struct WorkspacePaneView: View {
    @ObservedObject var workspace: WorkspaceStore
    let paneID: UUID
    @ObservedObject var services: AppServices
    @ObservedObject var webSessions: WebSessionStore
    let onConnect: (TunnelProfile) -> Void
    let onOpenTunnel: (TunnelProfile) -> Void
    let onStop: (String) -> Void
    let onOpenDirect: (String) -> Void
    let onSwitch: (UUID, WorkspaceTab) -> Void
    let onDelete: (TunnelProfile) -> Void
    @State private var activeDropDirection: SplitDirection?

    var body: some View {
        ZStack {
            VStack(spacing: 0) {
                tabStrip
                Divider()
                content
                    .id(workspace.selectedTab(in: paneID)?.id)
            }
            HStack(spacing: 0) {
                edgeDrop(direction: .left)
                Spacer(minLength: 0)
                edgeDrop(direction: .right)
            }
            .padding(.top, 36)
            .padding(.bottom, 36)
            VStack(spacing: 0) {
                edgeDrop(direction: .up)
                Spacer(minLength: 0)
                edgeDrop(direction: .down)
            }
            .padding(.top, 36)
            .padding(.bottom, 36)
        }
        .background(Color(nsColor: .textBackgroundColor))
        .onTapGesture { workspace.selectPane(paneID) }
    }

    private var tabs: [WorkspaceTab] { workspace.tabs(in: paneID) }

    private var tabStrip: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 3) {
                ForEach(tabs) { tab in
                    tabItem(tab)
                }
                Spacer(minLength: 0)
            }
            .padding(.horizontal, 6)
            .padding(.vertical, 4)
        }
        .frame(minHeight: 34, maxHeight: 36)
        .background(Color(nsColor: .windowBackgroundColor))
        .contentShape(Rectangle())
        .zIndex(2)
    }

    @ViewBuilder
    private var content: some View {
        if let tab = workspace.selectedTab(in: paneID) {
            if tab.kind == .management {
                ManagementView(
                    profiles: services.profiles,
                    tunnels: services.tunnels,
                    settings: services.settings,
                    keychain: services.keychain,
                    knownHosts: services.knownHosts,
                    onConnect: onConnect,
                     onOpenTunnel: onOpenTunnel,
                    onStop: onStop,
                    onOpenDirect: onOpenDirect,
                     onDelete: onDelete
                )
            } else {
                SessionView(
                    tab: tab,
                    profiles: services.profiles,
                    tunnels: services.tunnels,
                    webSessions: webSessions,
                    onClose: { closeTab(tab.id) }
                )
            }
        } else {
            Color(nsColor: .textBackgroundColor)
        }
    }

    private func tabItem(_ tab: WorkspaceTab) -> some View {
        let selected = workspace.selectedTab(in: paneID)?.id == tab.id
        return HStack(spacing: 5) {
            Text(title(for: tab))
                .lineLimit(1)
                .truncationMode(.middle)
            if tab.kind != .management {
                Button {
                    closeTab(tab.id)
                } label: {
                    Image(systemName: "xmark")
                        .font(.system(size: 9, weight: .semibold))
                }
                .buttonStyle(.plain)
                .help("关闭标签")
            }
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 5)
        .background(selected ? Color(nsColor: .controlAccentColor).opacity(0.16) : Color.clear, in: RoundedRectangle(cornerRadius: 5))
        .overlay(RoundedRectangle(cornerRadius: 5).stroke(selected ? Color(nsColor: .controlAccentColor).opacity(0.55) : Color(nsColor: .separatorColor), lineWidth: 1))
        .contentShape(Rectangle())
        .onTapGesture { workspace.selectTab(tab.id, in: paneID) }
        .zIndex(3)
        .modifier(WorkspaceTabDragModifier(tab: tab))
        .contextMenu { contextMenu(for: tab) }
    }

    @ViewBuilder
    private func contextMenu(for tab: WorkspaceTab) -> some View {
        if tab.kind != .management {
            Button("复制标签", systemImage: "plus.square.on.square") {
                workspace.duplicateTab(tab.id, to: paneID)
            }
            Button("重命名", systemImage: "pencil") {
                workspace.requestRename(tab)
            }
            Menu("拆分到") {
                splitButton("左侧", direction: .left, tab: tab)
                splitButton("右侧", direction: .right, tab: tab)
                splitButton("上方", direction: .up, tab: tab)
                splitButton("下方", direction: .down, tab: tab)
            }
            Menu("切换为") {
                ForEach(services.profiles.tunnels) { profile in
                    if tab.profileID != profile.id || tab.kind != .tunnel {
                        Button("隧道：\(profile.displayName)") {
                            onSwitch(tab.id, WorkspaceTab(kind: .tunnel, profileID: profile.id))
                        }
                    }
                }
                ForEach(services.profiles.directs, id: \.self) { url in
                    if tab.url != url || tab.kind != .direct {
                        Button("直连：\(ProfileStore.host(of: url))") {
                            onSwitch(tab.id, WorkspaceTab(kind: .direct, url: url))
                        }
                    }
                }
            }
            Divider()
            Button("关闭", systemImage: "xmark") { closeTab(tab.id) }
        }
    }

    private func splitButton(_ title: String, direction: SplitDirection, tab: WorkspaceTab) -> some View {
        Button(title) { workspace.splitPane(paneID, direction: direction, duplicateTabID: tab.id) }
    }

    private func title(for tab: WorkspaceTab) -> String {
        if let label = tab.label, !label.isEmpty { return label }
        switch tab.kind {
        case .management: return "管理"
        case .direct: return tab.url.map(ProfileStore.host(of:)) ?? "直连"
        case .tunnel:
            let name = services.profiles.findTunnel(id: tab.profileID)?.displayName ?? "隧道"
            let state = tab.profileID.map { services.tunnels.state(for: $0).status }
            let prefix: String
            switch state {
            case .some(.connected): prefix = "● "
            case .some(.failed): prefix = "× "
            case .some(.retrying): prefix = "↻ "
            case .some(.connecting), .some(.idle): prefix = "… "
            case .some(.stopped): prefix = "○ "
            case .none: prefix = ""
            }
            return prefix + name
        }
    }

    private func edgeDrop(direction: SplitDirection) -> some View {
        let highlighted = activeDropDirection == direction
        return Color(nsColor: .controlAccentColor)
            .opacity(highlighted ? 0.18 : 0.001)
            .frame(width: direction == .left || direction == .right ? 64 : nil,
                   height: direction == .up || direction == .down ? 64 : nil)
            .frame(maxWidth: direction == .up || direction == .down ? .infinity : nil,
                   maxHeight: direction == .left || direction == .right ? .infinity : nil)
            .onDrop(
                of: [UTType.plainText],
                isTargeted: Binding(
                    get: { activeDropDirection == direction },
                    set: { activeDropDirection = $0 ? direction : nil }
                )
            ) { providers, _ in
                activeDropDirection = nil
                return loadTabID(from: providers) { id in
                    workspace.moveTabToSplit(id, paneID: paneID, direction: direction)
                }
            }
    }

    private func closeTab(_ tabID: UUID) {
        webSessions.remove(tabID: tabID)
        workspace.closeTab(tabID)
    }

    private func loadTabID(from providers: [NSItemProvider], action: @escaping (UUID) -> Void) -> Bool {
        guard let provider = providers.first else { return false }
        provider.loadDataRepresentation(forTypeIdentifier: UTType.plainText.identifier) { data, _ in
            guard let data,
                  let value = String(data: data, encoding: .utf8),
                  let id = UUID(uuidString: value.trimmingCharacters(in: .whitespacesAndNewlines)) else { return }
            DispatchQueue.main.async { action(id) }
        }
        return true
    }
}

private struct WorkspaceTabDragModifier: ViewModifier {
    let tab: WorkspaceTab

    func body(content: Content) -> some View {
        if tab.kind == .management {
            content
        } else {
            content.onDrag {
                let provider = NSItemProvider()
                provider.registerDataRepresentation(
                    forTypeIdentifier: UTType.plainText.identifier,
                    visibility: .all
                ) { completion in
                    completion(Data(tab.id.uuidString.utf8), nil)
                    return nil
                }
                return provider
            }
        }
    }
}
