import XCTest
import SwiftUI
import AppKit
@testable import HarnessPortable

final class UIPreviewTests: XCTestCase {
    @MainActor
    func testManagementRendersInBothAppearances() throws {
        guard ProcessInfo.processInfo.environment["CI"] == "true",
              let output = ProcessInfo.processInfo.environment["HP_UI_PREVIEW_DIR"] else {
            throw XCTSkip("Visual export runs only on the isolated CI Mac.")
        }
        let directory = URL(fileURLWithPath: output, isDirectory: true)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        AppPaths.ensure()
        let previous = try? Data(contentsOf: AppPaths.profilesFile)
        defer {
            if let previous { try? previous.write(to: AppPaths.profilesFile) }
            else { try? FileManager.default.removeItem(at: AppPaths.profilesFile) }
        }
        let config = ProfileConfig(version: 1, tunnels: [
            TunnelProfile(id: "ui-dev", name: "开发工作站", sshHost: "workstation.local", user: "dev"),
            TunnelProfile(id: "ui-home", name: "家庭服务器", sshHost: "server.home", user: "admin", localPort: 4096)
        ], directs: ["http://localhost:3080", "https://docs.example.test/workspace"])
        try JSONEncoder().encode(config).write(to: AppPaths.profilesFile)
        let profiles = ProfileStore()
        let settings = SettingsStore()
        let keychain = KeychainStore()
        let knownHosts = KnownHostsStore()
        let tunnels = TunnelManager(keychain: keychain, knownHosts: knownHosts)
        let updater = UpdateService(settings: settings)
        for scheme in [ColorScheme.light, .dark] {
            let view = ManagementView(profiles: profiles, tunnels: tunnels, settings: settings,
                updater: updater, keychain: keychain, knownHosts: knownHosts,
                onConnect: { _ in }, onOpenTunnel: { _ in }, onStop: { _ in },
                onOpenDirect: { _ in }, onDelete: { _ in })
                .environment(\.colorScheme, scheme)
                .frame(width: 940, height: 690)
            let host = NSHostingView(rootView: view)
            let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 940, height: 690),
                                  styleMask: [.borderless], backing: .buffered, defer: false)
            window.isReleasedWhenClosed = false
            window.contentView = host
            window.orderFrontRegardless()
            host.layoutSubtreeIfNeeded()
            RunLoop.main.run(until: Date().addingTimeInterval(0.3))
            let bitmap = try XCTUnwrap(host.bitmapImageRepForCachingDisplay(in: host.bounds))
            host.cacheDisplay(in: host.bounds, to: bitmap)
            let data = try XCTUnwrap(bitmap.representation(using: .png, properties: [:]))
            XCTAssertGreaterThan(data.count, 10_000, "The preview must contain rendered UI, not an empty surface.")
            try data.write(to: directory.appendingPathComponent(scheme == .dark ? "macos-dark.png" : "macos-light.png"))
            window.close()
        }
    }
}
