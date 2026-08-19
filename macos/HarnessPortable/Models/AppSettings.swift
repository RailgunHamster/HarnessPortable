import Foundation

struct AppSettings: Codable {
    var version: Int = 1
    var closeBehavior: String = "exit"
    var restoreLastLayoutOnStartup: Bool = true
    var lastLayoutName: String = ""

    private enum CodingKeys: String, CodingKey {
        case version, closeBehavior, restoreLastLayoutOnStartup, lastLayoutName
    }

    init() {}

    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        version = try container.decodeIfPresent(Int.self, forKey: .version) ?? 1
        closeBehavior = try container.decodeIfPresent(String.self, forKey: .closeBehavior) ?? "exit"
        restoreLastLayoutOnStartup = try container.decodeIfPresent(Bool.self, forKey: .restoreLastLayoutOnStartup) ?? true
        lastLayoutName = try container.decodeIfPresent(String.self, forKey: .lastLayoutName) ?? ""
    }
}
