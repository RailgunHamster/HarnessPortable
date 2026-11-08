import Foundation
import CoreFoundation

enum SharedConfiguration {
    static let limit = 1024 * 1024
    static let filename = "harness-settings-v1.json"
    static func empty() -> [String: Any] { ["format": "harness-portable-settings", "version": 1, "entries": [String: Any]()] }
    static func entries(_ document: [String: Any]) -> [String: Any] { document["entries"] as? [String: Any] ?? [:] }
    static func same(_ a: Any?, _ b: Any?) -> Bool {
        guard let a, let b else { return a == nil && b == nil }
        return (try? JSONSerialization.data(withJSONObject: a, options: [.sortedKeys, .fragmentsAllowed])) == (try? JSONSerialization.data(withJSONObject: b, options: [.sortedKeys, .fragmentsAllowed]))
    }
    static func portableURL(_ raw: String) throws -> String {
        guard var c = URLComponents(string: raw), ["http", "https"].contains(c.scheme?.lowercased() ?? ""), !(c.host ?? "").isEmpty else { throw SyncError("直连地址无效") }
        if c.user == nil && c.password == nil && c.query == nil && c.fragment == nil { return raw }
        c.user = nil; c.password = nil; c.query = nil; c.fragment = nil
        guard let url = c.url else { throw SyncError("直连地址无效") }; return url.absoluteString
    }
    static let booleanSettings: Set<String> = ["windows:restoreLastLayoutOnStartup", "windows:reconnectRestoredWorkspace", "windows:connectionSidebarCollapsed", "macos:restoreLastLayoutOnStartup", "macos:reconnectRestoredWorkspace", "macos:connectionSidebarCollapsed", "android:browserFullscreen"]
    private static let tunnelFields: Set<String> = ["id", "name", "sshHost", "sshPort", "user", "remoteHost", "remotePort", "localPort", "authMode", "systemdUnit", "systemdScope", "systemdLogPath", "favorite", "group", "label", "rank"]
    private static let directFields: Set<String> = ["url", "favorite", "group", "label", "rank"]
    private static func isBool(_ value: Any?) -> Bool { guard let n = value as? NSNumber else { return false }; return CFGetTypeID(n) == CFBooleanGetTypeID() }
    private static func integer(_ value: Any?, range: ClosedRange<Int>) -> Bool {
        guard let n = value as? NSNumber, !isBool(value), n.doubleValue == Double(n.intValue) else { return false }; return range.contains(n.intValue)
    }
    static func validate(_ doc: [String: Any]) throws {
        try require(Set(doc.keys) == ["format", "version", "entries"] && doc["format"] as? String == "harness-portable-settings" && integer(doc["version"], range: 1...1), "不支持的同步文件格式")
        try require(try JSONSerialization.data(withJSONObject: doc).count <= limit && doc["entries"] is [String: Any], "同步文件无效或过大")
        let items = entries(doc); try require(items.count <= 2000, "同步项目超过上限")
        for (key, value) in items {
            try require(key.count <= 9000 && !key.unicodeScalars.contains(where: CharacterSet.controlCharacters.contains), "同步项目标识无效")
            if key.hasPrefix("tunnel:") || key.hasPrefix("direct:") {
                guard let item = value as? [String: Any] else { throw SyncError("连接格式无效") }
                let tunnel = key.hasPrefix("tunnel:"); let fields = tunnel ? tunnelFields : directFields
                try require(Set(item.keys) == fields, "连接含未知或敏感字段")
                for field in fields.subtracting(["favorite", "rank", "sshPort", "remotePort", "localPort"]) {
                    guard let s = item[field] as? String, s.count <= 8192, !s.unicodeScalars.contains(where: CharacterSet.controlCharacters.contains) else { throw SyncError("连接字段无效") }
                }
                try require(isBool(item["favorite"]) && integer(item["rank"], range: 0...1000000), "收藏或排序无效")
                if tunnel {
                    let id = item["id"] as! String
                    try require(key == "tunnel:" + id && id.range(of: "^[A-Za-z0-9_-]{1,120}$", options: .regularExpression) != nil && !(item["sshHost"] as! String).trimmingCharacters(in: .whitespaces).isEmpty && !(item["user"] as! String).trimmingCharacters(in: .whitespaces).isEmpty, "SSH 标识无效")
                    for field in ["sshPort", "remotePort", "localPort"] { try require(integer(item[field], range: 1...65535), "SSH 端口无效") }
                    try require(["nssm", "manual", "systemd", "launchd"].contains(item["authMode"] as! String), "认证模式无效")
                } else {
                    let url = item["url"] as! String; try require(key == "direct:" + url && portableURL(url) == url, "直连地址含凭据或登录参数")
                }
            } else if booleanSettings.contains(key) { try require(isBool(value), "偏好类型无效") }
            else {
                let choices: [String]
                switch key { case "shared:appearance": choices = ["system", "light", "dark"]; case "android:orientation": choices = ["auto", "portrait", "landscape"]; case "windows:closeBehavior", "macos:closeBehavior": choices = ["exit", "tray"]; default: choices = [] }
                try require(value is String && choices.contains(value as! String), "未知或无效的同步偏好")
            }
        }
    }
    static func merge(base: [String: Any], local: [String: Any], remote: [String: Any], choice: String? = nil) throws -> (document: [String: Any], conflicts: [String]) {
        try [base, local, remote].forEach(validate)
        let b = entries(base), l = entries(local), r = entries(remote); var output: [String: Any] = [:]; var conflicts: [String] = []
        for key in Set(b.keys).union(l.keys).union(r.keys).sorted() {
            let bv = b[key], lv = l[key], rv = r[key]; let selected: Any?
            if same(lv, rv) { selected = lv } else if same(lv, bv) { selected = rv } else if same(rv, bv) { selected = lv }
            else { conflicts.append(key); selected = choice == "remote" ? rv : lv }
            output[key] = selected
        }
        var doc = empty(); doc["entries"] = output; try validate(doc); return (doc, conflicts)
    }
    private static func require(_ condition: @autoclosure () throws -> Bool, _ message: String) throws { if try !condition() { throw SyncError(message) } }
}

struct SyncError: LocalizedError {
    let message: String
    init(_ message: String) { self.message = message }
    var errorDescription: String? { message }
}
