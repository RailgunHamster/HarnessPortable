import SwiftUI
import AppKit

@MainActor
final class AppRuntime {
    static let shared = AppRuntime()
    let services = AppServices()
}

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.regular)
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool {
        AppRuntime.shared.services.settings.value.closeBehavior != "tray"
    }

    func applicationWillTerminate(_ notification: Notification) {
        AppRuntime.shared.services.shutdown()
    }
}

@MainActor
@main
struct HarnessPortableApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var services: AppServices

    init() {
        _services = StateObject(wrappedValue: AppRuntime.shared.services)
    }

    var body: some Scene {
        Window("Harness Portable", id: "main") {
            WorkspaceView(services: services)
        }
        .commands {
            CommandGroup(replacing: .appTermination) {
                Button("退出 Harness Portable") {
                    services.shutdown()
                    NSApplication.shared.terminate(nil)
                }
                .keyboardShortcut("q")
            }
        }

        MenuBarExtra("Harness Portable", systemImage: "point.3.connected.trianglepath.dotted") {
            MenuBarView(services: services)
        }
    }
}

@MainActor
private struct MenuBarView: View {
    @ObservedObject var services: AppServices

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Button("打开工作区", systemImage: "macwindow") {
                NSApp.activate(ignoringOtherApps: true)
                NSApp.windows.first(where: { $0.title == "Harness Portable" })?.makeKeyAndOrderFront(nil)
            }

            if !services.tunnels.activeStates.isEmpty {
                Divider()
                ForEach(services.tunnels.activeStates) { state in
                    HStack {
                        Image(systemName: state.status == .connected ? "checkmark.circle.fill" : "arrow.triangle.2.circlepath")
                            .foregroundStyle(state.status == .connected ? Color.green : Color.orange)
                        Text(state.profileName ?? "隧道")
                        Spacer()
                        if state.localPort > 0 { Text(String(state.localPort)).foregroundStyle(.secondary) }
                    }
                    .frame(minWidth: 220)
                }
                Button("停止全部隧道", systemImage: "stop.fill") {
                    services.shutdown()
                }
            }

            Divider()
            Button("退出 Harness Portable", systemImage: "power") {
                services.shutdown()
                NSApplication.shared.terminate(nil)
            }
        }
        .padding(8)
    }
}
