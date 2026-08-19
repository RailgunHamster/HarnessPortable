import Foundation
import Combine

@MainActor
final class TunnelManager: ObservableObject {
    @Published private(set) var states: [String: TunnelInfo] = [:]

    private let keychain: KeychainStore
    private let knownHosts: KnownHostsStore
    private var engines: [String: SSHProcessTunnel] = [:]
    private var profiles: [String: TunnelProfile] = [:]

    init(keychain: KeychainStore, knownHosts: KnownHostsStore) {
        self.keychain = keychain
        self.knownHosts = knownHosts
    }

    func start(_ profile: TunnelProfile) {
        profiles[profile.id] = profile
        let engine = engine(for: profile.id)
        let current = states[profile.id]?.status
        if current == .connected || current == .connecting { return }
        engine.start(profile: profile)
    }

    func stop(_ profileID: String, announce: Bool = true) {
        guard let engine = engines[profileID], let profile = profiles[profileID] else { return }
        engine.stop(profile: profile)
        if !announce {
            states[profileID] = TunnelInfo(profileID: profileID, profileName: profile.displayName, status: .stopped)
        }
    }

    func stopAll() {
        for profileID in engines.keys {
            stop(profileID, announce: false)
        }
    }

    func state(for profileID: String) -> TunnelInfo {
        states[profileID] ?? TunnelInfo(profileID: profileID, status: .stopped)
    }

    var activeStates: [TunnelInfo] {
        states.values.filter { info in
            info.status == .connecting || info.status == .connected || info.status == .retrying
        }.sorted { ($0.profileName ?? "") < ($1.profileName ?? "") }
    }

    private func engine(for profileID: String) -> SSHProcessTunnel {
        if let existing = engines[profileID] { return existing }
        let engine = SSHProcessTunnel(keychain: keychain, knownHosts: knownHosts)
        engine.onState = { [weak self] info in
            DispatchQueue.main.async { [weak self] in
                self?.states[profileID] = info
            }
        }
        engines[profileID] = engine
        return engine
    }
}
