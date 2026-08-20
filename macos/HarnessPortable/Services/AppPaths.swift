import Foundation

enum AppPaths {
    static let dataDirectory: URL = {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        return base.appendingPathComponent("HarnessPortable", isDirectory: true)
    }()

    static var profilesFile: URL { dataDirectory.appendingPathComponent("profiles.json") }
    static var knownHostsFile: URL { dataDirectory.appendingPathComponent("known_hosts.json") }
    static var layoutsFile: URL { dataDirectory.appendingPathComponent("layouts.json") }
    static var settingsFile: URL { dataDirectory.appendingPathComponent("settings.json") }
    static var sshDiagnosticFile: URL { dataDirectory.appendingPathComponent("ssh-diagnostic.log") }
    static var crashLogFile: URL { dataDirectory.appendingPathComponent("crash.log") }
    static var supportDirectory: URL { dataDirectory.appendingPathComponent("Support", isDirectory: true) }

    static func ensure() {
        try? FileManager.default.createDirectory(at: dataDirectory, withIntermediateDirectories: true)
        try? FileManager.default.createDirectory(at: supportDirectory, withIntermediateDirectories: true)
        try? FileManager.default.setAttributes([.posixPermissions: NSNumber(value: Int(0o700))], ofItemAtPath: supportDirectory.path)
    }

    static func knownHostFile(for profileID: String) -> URL {
        supportDirectory.appendingPathComponent("known-host-\(profileID).tmp")
    }

    static var askpassScript: URL {
        supportDirectory.appendingPathComponent("ssh-askpass.sh")
    }

    static func passwordFile(for profileID: String) -> URL {
        supportDirectory.appendingPathComponent("ssh-password-\(profileID).tmp")
    }
}

func writeAtomically(_ data: Data, to url: URL) throws {
    let directory = url.deletingLastPathComponent()
    try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
    let temporary = directory.appendingPathComponent(".\(url.lastPathComponent).tmp-\(UUID().uuidString)")
    try data.write(to: temporary, options: .atomic)
    if FileManager.default.fileExists(atPath: url.path) {
        _ = try FileManager.default.replaceItemAt(url, withItemAt: temporary)
    } else {
        try FileManager.default.moveItem(at: temporary, to: url)
    }
}

func decodeJSON<T: Decodable>(_ type: T.Type, from data: Data) throws -> T {
    let object = try JSONSerialization.jsonObject(with: data)
    let normalized = normalizeJSONKeys(object)
    let normalizedData = try JSONSerialization.data(withJSONObject: normalized)
    return try JSONDecoder().decode(T.self, from: normalizedData)
}

private func normalizeJSONKeys(_ value: Any) -> Any {
    if let dictionary = value as? [String: Any] {
        return dictionary.reduce(into: [String: Any]()) { result, entry in
            let key = entry.key.isEmpty ? entry.key : String(entry.key.prefix(1)).lowercased() + String(entry.key.dropFirst())
            result[key] = normalizeJSONKeys(entry.value)
        }
    }
    if let array = value as? [Any] {
        return array.map(normalizeJSONKeys)
    }
    return value
}
