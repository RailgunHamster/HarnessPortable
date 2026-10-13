import Foundation

enum ConnectionOrdering {
    static func ordered(_ keys: [String], saved: [String]) -> [String] {
        let valid = Set(keys)
        var seen = Set<String>()
        return (saved + keys).filter { valid.contains($0) && seen.insert($0).inserted }
    }

    static func moving(_ key: String, to target: String, after: Bool, keys: [String], saved: [String]) -> [String]? {
        guard key != target, keys.contains(key), keys.contains(target) else { return nil }
        var result = ordered(keys, saved: saved).filter { $0 != key }
        guard let index = result.firstIndex(of: target) else { return nil }
        result.insert(key, at: index + (after ? 1 : 0))
        return result
    }
}
