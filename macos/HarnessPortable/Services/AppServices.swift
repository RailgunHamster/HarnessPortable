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
    let sync: ConfigurationSync

    private var didShutdown = false
    private var subscriptions = Set<AnyCancellable>()

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
        sync = ConfigurationSync(profiles: profiles, settings: settings, keychain: keychain)
        sync.startAutoSync()
        settings.objectWillChange.sink { [weak self] _ in self?.objectWillChange.send() }.store(in: &subscriptions)
        profiles.objectWillChange.sink { [weak self] _ in self?.objectWillChange.send() }.store(in: &subscriptions)
    }

    func stopAll() {
        tunnels.stopAll()
    }

    func shutdown() {
        guard !didShutdown else { return }
        didShutdown = true
        sync.cancel()
        tunnels.shutdown()
    }
}
