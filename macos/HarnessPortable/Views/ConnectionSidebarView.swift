import SwiftUI
import AppKit

private struct LibraryEntry: Identifiable {
    let id: String
    let name: String
    let address: String
    let profile: TunnelProfile?
    let url: String?
    let preference: ConnectionPreference
}

@MainActor
struct ConnectionSidebarView: View {
    @ObservedObject var profiles: ProfileStore
    @ObservedObject var tunnels: TunnelManager
    @ObservedObject var settings: SettingsStore
    let keychain: KeychainStore
    let onConnect: (TunnelProfile) -> Void
    let onReconnect: (TunnelProfile) -> Void
    let onOpenDirect: (String) -> Void
    let onDelete: (TunnelProfile) -> Void
    let onDeleteDirect: (String) -> Void
    let onSettings: () -> Void
    @Environment(\.colorScheme) private var colorScheme
    @State private var query = ""
    @State private var editingProfile: TunnelProfile?
    @State private var connectAfterDismiss: TunnelProfile?
    @State private var directEditor = false
    @State private var editingDirect: String?
    @State private var groupingKey: String?
    @State private var groupName = ""
    @State private var showGroupPrompt = false
    @State private var expandedGroups: Set<String> = ["未分组"]
    @State private var deleting: LibraryEntry?
    private var dsh: DshPalette { Dsh.palette(colorScheme) }

    private var entries: [LibraryEntry] {
        let tunnelEntries = profiles.tunnels.map { p in
            LibraryEntry(id: "tunnel:\(p.id)", name: p.displayName, address: "\(p.user)@\(p.sshHost):\(p.sshPort)", profile: p, url: nil,
                preference: settings.value.connections["tunnel:\(p.id)"] ?? ConnectionPreference())
        }
        let directEntries = profiles.directs.map { url in
            let preference = settings.value.connections["direct:\(url)"] ?? ConnectionPreference()
            return LibraryEntry(id: "direct:\(url)", name: preference.name.isEmpty ? ProfileStore.host(of: url) : preference.name,
                address: url, profile: nil, url: url, preference: preference)
        }
        return (tunnelEntries + directEntries).filter {
            query.isEmpty || ($0.name + " " + $0.address + " " + $0.preference.group).localizedCaseInsensitiveContains(query)
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text("连接库").font(.headline).foregroundStyle(dsh.textPrimary)
            TextField("搜索连接或服务器", text: $query).dshInput()
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 10) {
                    if query.isEmpty {
                        heading("常用", image: "star")
                        ForEach(entries.filter { $0.preference.favorite }) { row($0) }
                        heading("最近", image: "clock")
                        ForEach(Array(entries.filter { !$0.preference.lastOpened.isEmpty }.sorted { $0.preference.lastOpened > $1.preference.lastOpened }.prefix(6))) { row($0) }
                        heading("分组", image: "folder")
                        ForEach(Array(Set(entries.map { $0.preference.group.isEmpty ? "未分组" : $0.preference.group })).sorted(), id: \.self) { group in
                            DisclosureGroup(group, isExpanded: Binding(get: { expandedGroups.contains(group) }, set: { if $0 { expandedGroups.insert(group) } else { expandedGroups.remove(group) } })) {
                                ForEach(entries.filter { ($0.preference.group.isEmpty ? "未分组" : $0.preference.group) == group }) { row($0) }
                            }
                            .foregroundStyle(dsh.textSecondary)
                        }
                    } else {
                        heading("搜索结果", image: "magnifyingglass")
                        ForEach(entries) { row($0) }
                    }
                }
            }
            Menu {
                Button("SSH 隧道") { editingProfile = TunnelProfile() }
                Button("直连网页") { editingDirect = nil; directEditor = true }
            } label: { Label("添加连接", systemImage: "plus").frame(maxWidth: .infinity) }
            .menuStyle(.borderlessButton)
            .padding(10).background(dsh.accentSoft, in: RoundedRectangle(cornerRadius: 8))
            Button(action: onSettings) { Label("设置", systemImage: "slider.horizontal.3").frame(maxWidth: .infinity) }
                .buttonStyle(DshSecondaryButtonStyle())
        }
        .padding(16).frame(width: 240).background(dsh.bgLayer1)
        .sheet(item: $editingProfile, onDismiss: {
            if let profile = connectAfterDismiss { connectAfterDismiss = nil; onReconnect(profile) }
        }) { profile in
            ProfileEditorView(profile: profile, keychain: keychain, onConnect: { connectAfterDismiss = $0 }) { profiles.upsertTunnel($0) }
        }
        .sheet(isPresented: $directEditor) {
            DirectEditorView(url: editingDirect, profiles: profiles, settings: settings, onOpen: onOpenDirect)
        }
        .alert("设置连接分组", isPresented: $showGroupPrompt) {
            TextField("分组名称", text: $groupName)
            Button("取消", role: .cancel) {}
            Button("保存") {
                guard let key = groupingKey else { return }
                update(key) { $0.group = groupName.trimmingCharacters(in: .whitespacesAndNewlines) }
            }
        }
        .alert("删除连接？", isPresented: Binding(get: { deleting != nil }, set: { if !$0 { deleting = nil } })) {
            Button("取消", role: .cancel) { deleting = nil }
            Button("删除", role: .destructive) {
                guard let entry = deleting else { return }
                if let profile = entry.profile { onDelete(profile) } else if let url = entry.url { onDeleteDirect(url) }
                settings.update { $0.connections.removeValue(forKey: entry.id) }
                deleting = nil
            }
        } message: { Text("将删除 \(deleting?.name ?? "此连接") 的配置和对应凭据，并关闭它的网页。") }
    }

    private func heading(_ text: String, image: String) -> some View {
        Label(text, systemImage: image).font(.caption).foregroundStyle(dsh.textTertiary).padding(.top, 8)
    }
    private func row(_ entry: LibraryEntry) -> some View {
        let status = entry.profile.map { tunnels.state(for: $0.id).status }
        return HStack(spacing: 5) {
            Button {
                update(entry.id) { $0.lastOpened = ISO8601DateFormatter().string(from: Date()) }
                if let profile = entry.profile { onConnect(profile) } else if let url = entry.url { onOpenDirect(url) }
            } label: {
                HStack(spacing: 8) {
                    Image(systemName: entry.profile == nil ? "globe" : "server.rack").foregroundStyle(dsh.textSecondary)
                    VStack(alignment: .leading, spacing: 4) {
                        Text(entry.name).lineLimit(1).foregroundStyle(dsh.textPrimary)
                        Text(entry.profile == nil ? "直连网页" : status == .connected ? "SSH · 已连接" : "SSH · 未连接")
                            .font(.caption).foregroundStyle(status == .connected ? dsh.success : dsh.textSecondary)
                    }
                    Spacer(minLength: 0)
                }
                .padding(.vertical, 7)
            }
            .buttonStyle(.plain).help(entry.address)
            Menu {
                Button("编辑连接") {
                    if let profile = entry.profile { editingProfile = profile }
                    else { editingDirect = entry.url; directEditor = true }
                }
                Button(entry.preference.favorite ? "移出常用" : "加入常用") { update(entry.id) { $0.favorite.toggle() } }
                Button("设置分组…") { groupingKey = entry.id; groupName = entry.preference.group; showGroupPrompt = true }
                if let profile = entry.profile { Button("断开连接") { tunnels.stop(profile.id) } }
                Divider()
                Button("删除连接…", role: .destructive) { deleting = entry }
            } label: { Image(systemName: "ellipsis").frame(width: 20, height: 24) }
            .menuStyle(.borderlessButton).fixedSize()
        }
    }
    private func update(_ key: String, _ change: (inout ConnectionPreference) -> Void) {
        settings.update { value in var preference = value.connections[key] ?? ConnectionPreference(); change(&preference); value.connections[key] = preference }
    }
}

@MainActor
private struct DirectEditorView: View {
    let previous: String?
    @ObservedObject var profiles: ProfileStore
    @ObservedObject var settings: SettingsStore
    let onOpen: (String) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var address: String
    @State private var name: String
    @State private var group: String
    @State private var favorite: Bool
    @State private var authMode: String
    @State private var error = ""
    init(url: String?, profiles: ProfileStore, settings: SettingsStore, onOpen: @escaping (String) -> Void) {
        previous = url; self.profiles = profiles; self.settings = settings; self.onOpen = onOpen
        let preference = settings.value.connections["direct:\(url ?? "")"] ?? ConnectionPreference()
        _address = State(initialValue: url ?? ""); _name = State(initialValue: preference.name); _group = State(initialValue: preference.group)
        _favorite = State(initialValue: preference.favorite); _authMode = State(initialValue: settings.value.directAuthModes[url ?? ""] ?? LocalDshAuth.modeNone)
    }
    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("直连网页").font(.title2)
            Form {
                TextField("名称", text: $name)
                TextField("网页地址", text: $address)
                TextField("分组", text: $group)
                Picker("登录网页", selection: $authMode) { Text("在网页中登录").tag(LocalDshAuth.modeNone); Text("本机自动登录").tag(LocalDshAuth.modeLaunchd) }
                Toggle("加入常用", isOn: $favorite)
            }
            Text(error.isEmpty ? "保存后下次打开生效，当前网页保持。" : error).font(.callout)
            HStack { Spacer(); Button("取消") { dismiss() }; Button("保存") { save(open: false) }; Button("保存并打开") { save(open: true) } }
        }.padding(24).frame(width: 450)
    }
    private func save(open: Bool) {
        guard let normalized = ProfileStore.normalizeURL(address), let parsed = URL(string: normalized),
              ["http", "https"].contains(parsed.scheme?.lowercased() ?? ""), parsed.host != nil else { error = "请输入有效的 HTTP(S) 地址"; return }
        if let previous, previous != normalized { profiles.deleteDirect(previous) }
        guard let url = profiles.addDirect(normalized) else { return }
        settings.update { value in
            var preference = value.connections["direct:\(previous ?? url)"] ?? ConnectionPreference()
            preference.name = name.trimmingCharacters(in: .whitespacesAndNewlines); preference.group = group.trimmingCharacters(in: .whitespacesAndNewlines); preference.favorite = favorite
            if let previous { value.connections.removeValue(forKey: "direct:\(previous)"); value.directAuthModes.removeValue(forKey: previous) }
            value.connections["direct:\(url)"] = preference; value.directAuthModes[url] = authMode
        }
        dismiss()
        if open { onOpen(url) }
    }
}
