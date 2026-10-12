import Foundation

final class KnownHostsStore {
    enum Decision: Equatable {
        case trustedNew
        case trustedMatch
        case changed
        case unavailable
    }

    private struct Entry: Codable {
        var type: String
        var keyBase64: String
    }

    private struct FileFormat: Codable {
        var version: Int = 1
        var hosts: [String: Entry] = [:]
    }

    private let lock = NSLock()
    private let path: URL

    init(path: URL = AppPaths.knownHostsFile) { self.path = path }

    static func hostKeyID(host: String, port: Int) -> String {
        "\(host):\(port)"
    }

    func check(host: String, port: Int, keyType: String, keyBase64: String) -> Decision {
        let id = Self.hostKeyID(host: host, port: port)
        lock.lock()
        defer { lock.unlock() }

        guard var file = try? load() else { return .unavailable }
        if let stored = file.hosts[id] {
            return stored.type.caseInsensitiveCompare(keyType) == .orderedSame && stored.keyBase64 == keyBase64
                ? .trustedMatch
                : .changed
        }

        file.hosts[id] = Entry(type: keyType, keyBase64: keyBase64)
        do { try save(file) } catch { return .unavailable }
        return .trustedNew
    }

    func remove(host: String, port: Int) {
        let id = Self.hostKeyID(host: host, port: port)
        lock.lock()
        defer { lock.unlock() }
        guard var file = try? load() else { return }
        file.hosts.removeValue(forKey: id)
        try? save(file)
    }

    private func load() throws -> FileFormat {
        guard FileManager.default.fileExists(atPath: path.path) else { return FileFormat() }
        return try decodeJSON(FileFormat.self, from: Data(contentsOf: path))
    }

    private func save(_ file: FileFormat) throws {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try writeAtomically(encoder.encode(file), to: path)
    }
}
