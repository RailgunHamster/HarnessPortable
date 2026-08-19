import Foundation
import Combine

@MainActor
final class ProfileStore: ObservableObject {
    @Published private(set) var tunnels: [TunnelProfile] = []
    @Published private(set) var directs: [String] = []

    private(set) var version = 1

    init() {
        load()
    }

    func load() {
        guard let data = try? Data(contentsOf: AppPaths.profilesFile),
              let config = try? decodeJSON(ProfileConfig.self, from: data) else {
            tunnels = []
            directs = []
            return
        }

        version = config.version
        tunnels = config.tunnels.filter { !$0.sshHost.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
        directs = config.directs.filter { !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    }

    func save() {
        let config = ProfileConfig(version: version, tunnels: tunnels, directs: directs)
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try writeAtomically(encoder.encode(config), to: AppPaths.profilesFile)
        } catch {
            // Keep the in-memory configuration usable; the next save retries.
        }
    }

    func upsertTunnel(_ profile: TunnelProfile) {
        guard profile.isValid else { return }
        if let index = tunnels.firstIndex(where: { $0.id == profile.id }) {
            tunnels[index] = profile
        } else {
            tunnels.append(profile)
        }
        save()
    }

    func deleteTunnel(id: String) {
        tunnels.removeAll { $0.id == id }
        save()
    }

    @discardableResult
    func addDirect(_ raw: String) -> String? {
        guard let normalized = Self.normalizeURL(raw) else { return nil }
        if !directs.contains(where: { $0.caseInsensitiveCompare(normalized) == .orderedSame }) {
            directs.append(normalized)
            save()
        }
        return normalized
    }

    func deleteDirect(_ url: String) {
        directs.removeAll { $0 == url }
        save()
    }

    func findTunnel(id: String?) -> TunnelProfile? {
        guard let id else { return nil }
        return tunnels.first { $0.id == id }
    }

    nonisolated static func normalizeURL(_ raw: String) -> String? {
        let value = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { return nil }
        if value.range(of: "://") != nil {
            return value
        }
        return "http://\(value.contains(":") ? value : value + ":4096")"
    }

    nonisolated static func host(of url: String) -> String {
        guard let parsed = URL(string: url), let host = parsed.host, !host.isEmpty else {
            return url
        }
        return host
    }
}
