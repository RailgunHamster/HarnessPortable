import Foundation
import Combine
import AppKit
import CryptoKit

@MainActor
final class ConfigurationSync: ObservableObject {
    @Published private(set) var options = WebDavOptions()
    @Published private(set) var busy = false
    @Published private(set) var phase = "idle"
    @Published private(set) var message = "尚未同步"
    @Published private(set) var conflicts: [String] = []
    @Published private(set) var lastSuccess: Date?
    @Published private(set) var hasPassword = false
    private let profiles: ProfileStore
    private let settings: SettingsStore
    private let keychain: KeychainStore
    private let stateFile: URL
    private let journalFile: URL
    private let secretPrefix: String
    private let client = WebDavConfigurationClient()
    private var conflictLocal: [String: Any]?
    private var conflictRemote: [String: Any]?
    private var conflictETag: String?
    private var applying = false
    private var subscriptions = Set<AnyCancellable>()
    private var timer: Timer?
    private var pending: Task<Void, Never>?
    private var stateValid = true
    private struct State: Codable { var options = WebDavOptions(); var base = ""; var lastSuccess: Date? }
    private struct Journal: Codable { let profiles: ProfileConfig; let settings: AppSettings; let state: State }
    init(profiles: ProfileStore, settings: SettingsStore, keychain: KeychainStore, directory: URL = AppPaths.dataDirectory, secretPrefix: String = "#webdav-sync:") {
        self.profiles = profiles; self.settings = settings; self.keychain = keychain; self.secretPrefix = secretPrefix
        stateFile = directory.appendingPathComponent("settings-sync.json"); journalFile = directory.appendingPathComponent("settings-sync-rollback.json")
        do { try recover(); let state = try load(); options = state.options; lastSuccess = state.lastSuccess; hasPassword = keychain.hasPassword(for: secretKey(state.options)) }
        catch { stateValid = false; phase = "failed"; message = "本机同步记录或恢复文件损坏，未应用云端配置；请保留数据目录中的同步文件。" }
    }
    private func secretKey(_ options: WebDavOptions) -> String { secretPrefix + SHA256.hash(data: Data((options.url + "\n" + options.username).utf8)).map { String(format: "%02x", $0) }.joined() }
    private func load() throws -> State {
        guard FileManager.default.fileExists(atPath: stateFile.path) else { return State() }
        let state = try JSONDecoder().decode(State.self, from: Data(contentsOf: stateFile)); _ = try baseline(state); return state
    }
    private func baseline(_ state: State) throws -> [String: Any] {
        if state.base.isEmpty { return SharedConfiguration.empty() }
        guard let doc = try JSONSerialization.jsonObject(with: Data(state.base.utf8)) as? [String: Any] else { throw SyncError("本机同步记录损坏") }
        try SharedConfiguration.validate(doc); return doc
    }
    func configure(_ value: WebDavOptions, password: String? = nil, clearPassword: Bool = false) throws {
        guard !busy && stateValid else { throw SyncError("请等待同步完成；损坏的本机记录需先恢复。") }
        var value = value; value.url = value.url.trimmingCharacters(in: .whitespacesAndNewlines); value.username = value.username.trimmingCharacters(in: .whitespacesAndNewlines)
        if !value.url.isEmpty || value.autoSync { _ = try WebDavConfigurationClient.folder(value) }
        guard (password?.count ?? 0) <= 8192 else { throw SyncError("同步密码过长") }
        var state = try load(); let same = value.url == state.options.url && value.username == state.options.username
        guard same || password != nil || clearPassword || !keychain.hasPassword(for: secretKey(state.options)) else { throw SyncError("更换 WebDAV 地址或账号时，请重填同步密码或清除密码。") }
        if clearPassword { keychain.deletePassword(for: secretKey(value)) }
        else if let password { try keychain.setPassword(password, for: secretKey(value)) }
        if !same { state.base = ""; state.lastSuccess = nil }
        state.options = value; try writeAtomically(JSONEncoder().encode(state), to: stateFile)
        options = value; lastSuccess = state.lastSuccess; hasPassword = keychain.hasPassword(for: secretKey(value)); conflicts = []; conflictLocal = nil
        phase = "idle"; message = "同步设置已保存 · 连接密码始终只保存在本机"
        schedule()
    }
    func cancel() { client.cancel() }
    func test() async {
        await execute { state, password in try await self.client.test(state.options, password: password); self.message = "WebDAV 目录可访问；首次同步会验证写入权限。" }
    }
    func run(choice: String? = nil, automatic: Bool = false) async {
        if automatic && (!options.autoSync || busy || phase == "conflict" || !NSApp.isActive) { return }
        await execute { state, password in
            guard choice == nil || ["local", "remote"].contains(choice!) && self.conflictLocal != nil else { throw SyncError("请先查看同步冲突再选择保留哪一端。") }
            let base = try self.baseline(state); let local = try self.snapshot(base)
            guard choice == nil || SharedConfiguration.same(local, self.conflictLocal) else { throw SyncError("本机已改变，请重新同步并确认冲突。") }
            for _ in 0..<3 {
                let remote = try await self.client.read(state.options, password: password)
                guard choice == nil || remote.etag == self.conflictETag && SharedConfiguration.same(remote.document, self.conflictRemote) else { throw SyncError("云端已改变，请重新同步并确认冲突。") }
                let merge = try SharedConfiguration.merge(base: remote.missing ? SharedConfiguration.empty() : base, local: local, remote: remote.document, choice: choice)
                if !merge.conflicts.isEmpty && choice == nil {
                    self.conflictLocal = local; self.conflictRemote = remote.document; self.conflictETag = remote.etag; self.conflicts = merge.conflicts
                    self.phase = "conflict"; self.message = "两端修改了相同项目，请选择冲突项保留哪一端；其余项目照常合并。"; return
                }
                guard SharedConfiguration.same(local, try self.snapshot(base)) else { throw SyncError("同步期间本机配置已改变，未覆盖本机修改，请重试。") }
                if remote.missing || !SharedConfiguration.same(merge.document, remote.document) {
                    let written = try await self.client.write(state.options, password: password, document: merge.document, etag: remote.etag, missing: remote.missing)
                    if !written {
                        if choice != nil { throw SyncError("云端已改变，请重新确认冲突。") }; continue
                    }
                }
                guard SharedConfiguration.same(local, try self.snapshot(base)) else { throw SyncError("上传期间本机配置已改变；本机修改已保留，请重新同步。") }
                try self.client.checkCancelled(); try self.apply(merge.document, state: state)
                self.phase = "success"; self.conflicts = []; self.conflictLocal = nil; self.message = "链接库、收藏分组排序及偏好已同步 · 密码和私钥留在本机"; return
            }
            throw SyncError("其他设备正在修改云端配置，请稍后重试。")
        }
    }
    private func execute(_ operation: (State, String) async throws -> Void) async {
        guard !busy else { return }; busy = true; phase = "syncing"; message = "正在同步…"; client.begin()
        defer { busy = false }
        do {
            guard stateValid && !FileManager.default.fileExists(atPath: journalFile.path) else { throw SyncError("本机同步恢复尚未完成，请重启应用。") }
            let state = try load(); _ = try WebDavConfigurationClient.folder(state.options)
            try await operation(state, keychain.password(for: secretKey(state.options)) ?? "")
            if phase == "syncing" { phase = "success" }
        } catch is CancellationError { phase = "cancelled"; message = "同步已取消，本机配置未替换；下次同步会重新检查云端。" }
        catch {
            phase = client.isCancelled() ? "cancelled" : "failed"
            message = client.isCancelled() ? "同步已取消，本机配置未替换；下次同步会重新检查云端。" : (error as? SyncError)?.message ?? "同步失败，未应用云端配置；请检查网络与 WebDAV 设置。"
        }
    }
    func snapshot(_ base: [String: Any]) throws -> [String: Any] {
        var entries = SharedConfiguration.entries(base).filter { $0.key.hasPrefix("windows:") || $0.key.hasPrefix("android:") }
        let preferences = settings.value
        let keys = profiles.tunnels.map { "tunnel:" + $0.id } + profiles.directs.map { "direct:" + $0 }
        let order = ConnectionOrdering.ordered(keys, saved: preferences.connectionOrder)
        func metadata(_ value: [String: Any], _ key: String) -> [String: Any] {
            var item = value; let p = preferences.connections[key] ?? ConnectionPreference()
            item["favorite"] = p.favorite; item["group"] = p.group; item["label"] = p.name; item["rank"] = order.firstIndex(of: key) ?? 0; return item
        }
        for p in profiles.tunnels {
            let key = "tunnel:" + p.id
            entries[key] = metadata(["id": p.id, "name": p.name, "sshHost": p.sshHost, "sshPort": p.sshPort, "user": p.user,
                "remoteHost": p.remoteHost, "remotePort": p.remotePort, "localPort": p.localPort, "authMode": p.authMode,
                "systemdUnit": p.systemdUnit, "systemdScope": p.systemdScope, "systemdLogPath": p.systemdLogPath], key)
        }
        for url in profiles.directs {
            let safe = try SharedConfiguration.portableURL(url); let key = "direct:" + safe
            guard entries[key] == nil else { throw SyncError("直连地址去除登录参数后重复，请使用不同网页路径。") }
            entries[key] = metadata(["url": safe], "direct:" + url)
        }
        entries["shared:appearance"] = preferences.appearance; entries["macos:closeBehavior"] = preferences.closeBehavior
        entries["macos:restoreLastLayoutOnStartup"] = preferences.restoreLastLayoutOnStartup
        entries["macos:reconnectRestoredWorkspace"] = preferences.reconnectRestoredWorkspace
        entries["macos:connectionSidebarCollapsed"] = preferences.connectionSidebarCollapsed
        var doc = SharedConfiguration.empty(); doc["entries"] = entries; try SharedConfiguration.validate(doc); return doc
    }
    private func sameIdentity(_ a: TunnelProfile, _ b: TunnelProfile) -> Bool { a.sshHost.lowercased() == b.sshHost.lowercased() && a.sshPort == b.sshPort && a.user == b.user }
    private func apply(_ doc: [String: Any], state: State) throws {
        try SharedConfiguration.validate(doc); let entries = SharedConfiguration.entries(doc)
        let before = ProfileConfig(tunnels: profiles.tunnels, directs: profiles.directs); let oldSettings = settings.value; var next = ProfileConfig(); var preferences = oldSettings
        let keys = entries.keys.filter { $0.hasPrefix("tunnel:") || $0.hasPrefix("direct:") }.sorted { a, b in let x = (entries[a] as! [String: Any])["rank"] as! Int; let y = (entries[b] as! [String: Any])["rank"] as! Int; return x == y ? a < b : x < y }
        var order: [String] = []
        for key in keys {
            let item = entries[key] as! [String: Any]; var localKey = key
            if key.hasPrefix("tunnel:") {
                var profile = try JSONDecoder().decode(TunnelProfile.self, from: JSONSerialization.data(withJSONObject: item))
                if let old = before.tunnels.first(where: { $0.id == profile.id && sameIdentity($0, profile) }) { profile.identityFile = old.identityFile }
                next.tunnels.append(profile)
            } else {
                let url = item["url"] as! String; let retained = before.directs.first { (try? SharedConfiguration.portableURL($0)) == url } ?? url
                next.directs.append(retained); localKey = "direct:" + retained
            }
            var p = preferences.connections[localKey] ?? ConnectionPreference(); p.favorite = item["favorite"] as! Bool; p.group = item["group"] as! String; p.name = item["label"] as! String
            preferences.connections[localKey] = p; order.append(localKey)
        }
        preferences.connectionOrder = order
        if let value = entries["shared:appearance"] as? String { preferences.appearance = value }
        if let value = entries["macos:closeBehavior"] as? String { preferences.closeBehavior = value }
        if let value = entries["macos:restoreLastLayoutOnStartup"] as? Bool { preferences.restoreLastLayoutOnStartup = value }
        if let value = entries["macos:reconnectRestoredWorkspace"] as? Bool { preferences.reconnectRestoredWorkspace = value; preferences.startupReconnectPreference = value }
        if let value = entries["macos:connectionSidebarCollapsed"] as? Bool { preferences.connectionSidebarCollapsed = value }
        try writeAtomically(JSONEncoder().encode(Journal(profiles: before, settings: oldSettings, state: state)), to: journalFile)
        applying = true; defer { applying = false }
        do {
            for old in before.tunnels {
                if let current = next.tunnels.first(where: { $0.id == old.id }), sameIdentity(old, current) {
                    if old.remoteHost != current.remoteHost || old.remotePort != current.remotePort { keychain.deleteAuthInput(for: old.id) }
                } else { keychain.deletePassword(for: old.id); keychain.deleteAuthInput(for: old.id) }
            }
            try profiles.replaceForSync(next); try settings.replaceForSync(preferences)
            var updated = state; updated.base = String(data: try JSONSerialization.data(withJSONObject: doc, options: [.sortedKeys]), encoding: .utf8)!; updated.lastSuccess = Date()
            try writeAtomically(JSONEncoder().encode(updated), to: stateFile); try FileManager.default.removeItem(at: journalFile); lastSuccess = updated.lastSuccess
        } catch { try recover(); throw error }
    }
    private func recover() throws {
        guard FileManager.default.fileExists(atPath: journalFile.path) else { return }
        let journal = try JSONDecoder().decode(Journal.self, from: Data(contentsOf: journalFile))
        try profiles.replaceForSync(journal.profiles); try settings.replaceForSync(journal.settings)
        try writeAtomically(JSONEncoder().encode(journal.state), to: stateFile); try FileManager.default.removeItem(at: journalFile)
        phase = "failed"; message = "上次同步未完成，已恢复本机配置；缺少的连接密码需在本机补填。"
    }
    func startAutoSync() {
        guard timer == nil else { return }
        profiles.$tunnels.dropFirst().sink { [weak self] _ in self?.schedule() }.store(in: &subscriptions)
        profiles.$directs.dropFirst().sink { [weak self] _ in self?.schedule() }.store(in: &subscriptions)
        settings.$value.dropFirst().sink { [weak self] _ in self?.schedule() }.store(in: &subscriptions)
        NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification).sink { [weak self] _ in Task { @MainActor in await self?.run(automatic: true) } }.store(in: &subscriptions)
        timer = Timer.scheduledTimer(withTimeInterval: 300, repeats: true) { [weak self] _ in Task { @MainActor in await self?.run(automatic: true) } }
        schedule()
    }
    private func schedule() {
        if applying || busy { return }; pending?.cancel()
        if phase == "conflict" { phase = "idle"; conflictLocal = nil }
        pending = Task { try? await Task.sleep(nanoseconds: 2_000_000_000); if !Task.isCancelled { await run(automatic: true) } }
    }
}
