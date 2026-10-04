import SwiftUI
import AppKit

/// Hosts the management UI in its own window. Actions that need workspace
/// context (opening tabs, password prompts) are forwarded to the workspace
/// window through NotificationCenter, so both windows stay functional even
/// when only one of them is open.
@MainActor
struct ManagementWindowView: View {
    @ObservedObject var services: AppServices
    @Environment(\.openWindow) private var openWindow

    @State private var passwordProfile: TunnelProfile?
    @State private var passwordRequestID = UUID()
    @State private var keychainError: String?

    @Environment(\.colorScheme) var colorScheme

    private var dsh: DshPalette { Dsh.palette(colorScheme) }

    var body: some View {
        ManagementView(
            profiles: services.profiles,
            tunnels: services.tunnels,
            settings: services.settings,
            updater: services.updater,
            keychain: services.keychain,
            knownHosts: services.knownHosts,
            onConnect: connect,
            onOpenTunnel: openTunnel,
            onStop: { services.tunnels.stop($0) },
            onOpenDirect: openDirect,
            onDelete: deleteProfile
        )
        .sheet(item: $passwordProfile, onDismiss: { passwordRequestID = UUID() }) { profile in
            let requestID = passwordRequestID
            PasswordPromptView(profile: profile) { password in
                guard passwordRequestID == requestID,
                      passwordProfile?.id == profile.id,
                      services.profiles.findTunnel(id: profile.id) != nil else { return false }
                do {
                    try services.keychain.setPassword(password, for: profile.id)
                    guard passwordRequestID == requestID,
                          passwordProfile?.id == profile.id else { return false }
                    requestOpenTab(for: profile)
                    services.tunnels.start(profile)
                    return true
                } catch {
                    keychainError = error.localizedDescription
                    return false
                }
            }
        }
        .alert("无法保存 SSH 密码", isPresented: keychainErrorPresented) {
            Button("确定", role: .cancel) { keychainError = nil }
        } message: {
            Text(keychainError ?? "未知 Keychain 错误")
        }
    }

    private var keychainErrorPresented: Binding<Bool> {
        Binding(
            get: { keychainError != nil },
            set: { isPresented in
                if !isPresented { keychainError = nil }
            }
        )
    }

    private func canStartWithoutPrompt(_ profile: TunnelProfile) -> Bool {
        services.keychain.hasPassword(for: profile.id) ||
            SSHIdentity.hasUsableKey(identityFile: profile.identityFile)
    }

    private func connect(_ profile: TunnelProfile) {
        if canStartWithoutPrompt(profile) {
            requestOpenTab(for: profile)
            services.tunnels.start(profile)
        } else {
            passwordRequestID = UUID()
            passwordProfile = profile
        }
    }

    private func openTunnel(_ profile: TunnelProfile) {
        if canStartWithoutPrompt(profile) {
            requestOpenTab(for: profile)
            services.tunnels.start(profile)
        } else {
            passwordRequestID = UUID()
            passwordProfile = profile
        }
    }

    private func openDirect(_ url: String) {
        NSApp.activate(ignoringOtherApps: true)
        openWindow(id: "main")
        NotificationCenter.default.post(name: .openDirectTabRequest, object: url)
    }

    private func requestOpenTab(for profile: TunnelProfile) {
        NSApp.activate(ignoringOtherApps: true)
        openWindow(id: "main")
        NotificationCenter.default.post(name: .openTunnelTabRequest, object: profile.id)
    }

    private func deleteProfile(_ profile: TunnelProfile) {
        services.tunnels.stop(profile.id)
        services.keychain.deletePassword(for: profile.id)
        services.keychain.deleteAuthInput(for: profile.id)
        services.profiles.deleteTunnel(id: profile.id)
    }
}
