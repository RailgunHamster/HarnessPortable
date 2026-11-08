import Foundation
import Combine

@MainActor
final class SettingsStore: ObservableObject {
    @Published private(set) var value: AppSettings
    private let path: URL

    init(path: URL = AppPaths.settingsFile) {
        self.path = path
        if let data = try? Data(contentsOf: path),
           let settings = try? decodeJSON(AppSettings.self, from: data) {
            value = settings
        } else {
            value = AppSettings()
        }
    }

    func update(_ change: (inout AppSettings) -> Void) {
        var next = value
        change(&next)
        value = next
        save()
    }

    func save() {
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try writeAtomically(encoder.encode(value), to: path)
        } catch {
            // The running app keeps the updated value and retries on the next change.
        }
    }

    func replaceForSync(_ settings: AppSettings) throws {
        let encoder = JSONEncoder(); encoder.outputFormatting = [.sortedKeys, .prettyPrinted]
        try writeAtomically(encoder.encode(settings), to: path); value = settings
    }
}
