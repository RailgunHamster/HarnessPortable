import Foundation

enum TunnelStatus: String, Equatable {
    case idle
    case connecting
    case connected
    case retrying
    case failed
    case stopped
}

struct TunnelInfo: Identifiable, Equatable {
    var profileID: String?
    var profileName: String?
    var status: TunnelStatus
    var message: String?
    var localPort: Int

    var id: String { profileID ?? "none" }

    init(
        profileID: String? = nil,
        profileName: String? = nil,
        status: TunnelStatus = .idle,
        message: String? = nil,
        localPort: Int = 0
    ) {
        self.profileID = profileID
        self.profileName = profileName
        self.status = status
        self.message = message
        self.localPort = localPort
    }
}
