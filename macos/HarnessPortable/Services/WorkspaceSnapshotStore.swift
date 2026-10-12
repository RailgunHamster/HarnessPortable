import Foundation

struct WorkspaceSnapshotStore {
    let url: URL
    init(url: URL = AppPaths.dataDirectory.appendingPathComponent("workspace.json")) { self.url = url }
    func load() -> WorkspaceLayout? {
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? decodeJSON(WorkspaceLayout.self, from: data)
    }
    func save(_ layout: WorkspaceLayout) {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        if let data = try? encoder.encode(layout) { try? writeAtomically(data, to: url) }
    }
}
