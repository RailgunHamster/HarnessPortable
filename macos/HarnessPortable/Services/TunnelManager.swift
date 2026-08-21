import Foundation
import Combine

@MainActor
final class TunnelManager: ObservableObject {
    @Published private(set) var states: [String: TunnelInfo] = [:]

    private let keychain: KeychainStore
    private let knownHosts: KnownHostsStore
    private var engines: [String: SSHProcessTunnel] = [:]
    private var profiles: [String: TunnelProfile] = [:]
    private var isShuttingDown = false

    init(keychain: KeychainStore, knownHosts: KnownHostsStore) {
        self.keychain = keychain
        self.knownHosts = knownHosts
    }

    func start(_ profile: TunnelProfile) {
        guard !isShuttingDown else { return }
        profiles[profile.id] = profile
        let current = states[profile.id]?.status
        if current == .connected || current == .connecting || current == .retrying { return }
        let engine = engine(for: profile.id)
        let generation = engine.start(profile: profile)
        states[profile.id] = TunnelInfo(
            profileID: profile.id,
            profileName: profile.displayName,
            status: .connecting,
            message: "正在连接…",
            generation: generation
        )
    }

    func stop(_ profileID: String, announce: Bool = true) {
        guard let engine = engines[profileID], let profile = profiles[profileID] else { return }
        let generation = engine.stop(profile: profile)
        states[profileID] = TunnelInfo(
            profileID: profileID,
            profileName: profile.displayName,
            status: .stopped,
            message: announce ? "隧道已停止" : nil,
            generation: generation
        )
    }

    func stopAll() {
        let profileIDs = Array(engines.keys)
        for profileID in profileIDs {
            stop(profileID, announce: false)
        }
    }

    func shutdown() {
        guard !isShuttingDown else { return }
        isShuttingDown = true
        let currentEngines = engines
        for (profileID, engine) in currentEngines {
            if let profile = profiles[profileID] {
                engine.shutdown(profile: profile)
            } else {
                engine.shutdown()
            }
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
        engine.onState = { [weak self] info, generation in
            DispatchQueue.main.async { [weak self] in
                guard let self,
                      !self.isShuttingDown,
                      self.engines[profileID]?.isCurrent(generation) == true else { return }
                self.states[profileID] = info
            }
        }
        engines[profileID] = engine
        return engine
    }
}
