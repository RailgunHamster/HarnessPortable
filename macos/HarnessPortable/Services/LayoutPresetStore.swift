import Foundation
import Combine

@MainActor
final class LayoutPresetStore: ObservableObject {
    @Published private(set) var layouts: [WorkspaceLayout] = []

    init() {
        load()
    }

    func load() {
        guard let data = try? Data(contentsOf: AppPaths.layoutsFile),
              let decoded = try? decodeJSON([WorkspaceLayout].self, from: data) else {
            layouts = []
            return
        }
        layouts = decoded
    }

    func upsert(_ layout: WorkspaceLayout) {
        layouts.removeAll { $0.name.caseInsensitiveCompare(layout.name) == .orderedSame }
        layouts.insert(layout, at: 0)
        save()
    }

    func delete(name: String) {
        layouts.removeAll { $0.name.caseInsensitiveCompare(name) == .orderedSame }
        save()
    }

    func save() {
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try writeAtomically(encoder.encode(layouts), to: AppPaths.layoutsFile)
        } catch {
            // Keep the current presets available in memory.
        }
    }

    func named(_ name: String) -> WorkspaceLayout? {
        layouts.first { $0.name.caseInsensitiveCompare(name) == .orderedSame }
    }
}
