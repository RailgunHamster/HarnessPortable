import Foundation

final class KnownHostsStore {
    enum Decision {
        case trustedNew
        case trustedMatch
        case changed
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

    static func hostKeyID(host: String, port: Int) -> String {
        "\(host):\(port)"
    }

    func check(host: String, port: Int, keyType: String, keyBase64: String) -> Decision {
        let id = Self.hostKeyID(host: host, port: port)
        lock.lock()
        defer { lock.unlock() }

        var file = load()
        if let stored = file.hosts[id] {
            return stored.type.caseInsensitiveCompare(keyType) == .orderedSame && stored.keyBase64 == keyBase64
                ? .trustedMatch
                : .changed
        }

        file.hosts[id] = Entry(type: keyType, keyBase64: keyBase64)
        save(file)
        return .trustedNew
    }

    func remove(host: String, port: Int) {
        let id = Self.hostKeyID(host: host, port: port)
        lock.lock()
        defer { lock.unlock() }
        var file = load()
        file.hosts.removeValue(forKey: id)
        save(file)
    }

    private func load() -> FileFormat {
        guard let data = try? Data(contentsOf: AppPaths.knownHostsFile),
              let decoded = try? decodeJSON(FileFormat.self, from: data) else {
            return FileFormat()
        }
        return decoded
    }

    private func save(_ file: FileFormat) {
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try writeAtomically(encoder.encode(file), to: AppPaths.knownHostsFile)
        } catch {
            // A failed write leaves the in-memory decision intact for this session.
        }
    }
}
