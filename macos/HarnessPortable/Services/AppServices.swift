import Foundation
import Combine

@MainActor
final class AppServices: ObservableObject {
    let profiles: ProfileStore
    let settings: SettingsStore
    let layouts: LayoutPresetStore
    let keychain: KeychainStore
    let knownHosts: KnownHostsStore
    let tunnels: TunnelManager
    let updater: UpdateService

    private var didShutdown = false

    init() {
        AppPaths.ensure()
        SSHProcessTunnel.cleanupOrphanedSSHProcesses()
        profiles = ProfileStore()
        settings = SettingsStore()
        layouts = LayoutPresetStore()
        keychain = KeychainStore()
        knownHosts = KnownHostsStore()
        tunnels = TunnelManager(keychain: keychain, knownHosts: knownHosts)
        updater = UpdateService(settings: settings)
    }

    func stopAll() {
        tunnels.stopAll()
    }

    func shutdown() {
        guard !didShutdown else { return }
        didShutdown = true
        tunnels.shutdown()
    }
}
