import Foundation
import Combine

@MainActor
final class SettingsStore: ObservableObject {
    @Published private(set) var value: AppSettings

    init() {
        if let data = try? Data(contentsOf: AppPaths.settingsFile),
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
            try writeAtomically(encoder.encode(value), to: AppPaths.settingsFile)
        } catch {
            // The running app keeps the updated value and retries on the next change.
        }
    }
}
