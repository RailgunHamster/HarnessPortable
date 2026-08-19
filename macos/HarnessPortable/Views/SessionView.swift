import Foundation
import Combine
import WebKit
import SwiftUI

@MainActor
final class WebSessionStore: ObservableObject {
    private let processPool = WKProcessPool()
    private var webViews: [UUID: WKWebView] = [:]
    private var loadedURLs: [UUID: String] = [:]

    func webView(for tabID: UUID) -> WKWebView {
        if let existing = webViews[tabID] { return existing }
        let configuration = WKWebViewConfiguration()
        configuration.processPool = processPool
        configuration.websiteDataStore = .default()
        configuration.preferences.isDeveloperExtrasEnabled = true
        let webView = WKWebView(frame: .zero, configuration: configuration)
        webView.allowsBackForwardNavigationGestures = true
        webViews[tabID] = webView
        return webView
    }

    func load(tab: WorkspaceTab, profile: TunnelProfile?, state: TunnelInfo) {
        let destination: String?
        switch tab.kind {
        case .management:
            destination = nil
        case .direct:
            destination = tab.url.flatMap { HostResolver.resolveURL($0) ?? $0 }
        case .tunnel:
            let port = state.localPort > 0 ? state.localPort : (profile?.localPort ?? 0)
            destination = port > 0 ? "http://127.0.0.1:\(port)" : nil
        }
        guard let destination, let url = URL(string: destination) else { return }
        guard loadedURLs[tab.id] != destination else { return }
        loadedURLs[tab.id] = destination
        webView(for: tab.id).load(URLRequest(url: url))
    }

    func reload(tabID: UUID) {
        webViews[tabID]?.reload()
    }

    func remove(tabID: UUID) {
        webViews[tabID]?.stopLoading()
        webViews.removeValue(forKey: tabID)
        loadedURLs.removeValue(forKey: tabID)
    }
}

@MainActor
struct WebViewContainer: NSViewRepresentable {
    let webView: WKWebView

    func makeNSView(context: Context) -> WKWebView { webView }
    func updateNSView(_ nsView: WKWebView, context: Context) {}
}

@MainActor
struct SessionView: View {
    let tab: WorkspaceTab
    @ObservedObject var profiles: ProfileStore
    @ObservedObject var tunnels: TunnelManager
    @ObservedObject var webSessions: WebSessionStore
    let onClose: () -> Void

    private var profile: TunnelProfile? {
        profiles.findTunnel(id: tab.profileID)
    }

    private var tunnelInfo: TunnelInfo {
        guard let profileID = tab.profileID else { return TunnelInfo(status: .stopped) }
        return tunnels.state(for: profileID)
    }

    var body: some View {
        ZStack(alignment: .topTrailing) {
            WebViewContainer(webView: webSessions.webView(for: tab.id))
                .background(Color(nsColor: .textBackgroundColor))

            HStack(spacing: 6) {
                Button {
                    webSessions.reload(tabID: tab.id)
                } label: {
                    Image(systemName: "arrow.clockwise")
                }
                .help("刷新")
                .buttonStyle(.borderless)

                Button {
                    onClose()
                } label: {
                    Image(systemName: "xmark")
                }
                .help("关闭标签")
                .buttonStyle(.borderless)
            }
            .padding(8)
            .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 6))
            .padding(8)

            if tab.kind == .tunnel && tunnelInfo.status != .connected {
                tunnelOverlay
            }
        }
        .onAppear { loadIfNeeded() }
        .onChange(of: tunnelInfo.status) { _ in loadIfNeeded() }
        .onChange(of: tunnelInfo.localPort) { _ in loadIfNeeded() }
    }

    @ViewBuilder
    private var tunnelOverlay: some View {
        VStack(spacing: 10) {
            Image(systemName: statusSymbol)
                .font(.system(size: 28, weight: .medium))
                .foregroundStyle(statusColor)
            Text(statusTitle)
                .font(.headline)
            if let message = tunnelInfo.message, !message.isEmpty {
                Text(message)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .multilineTextAlignment(.center)
            }
            HStack {
                if tunnelInfo.status == .failed || tunnelInfo.status == .retrying,
                   let profile {
                    Button("重新连接") { tunnels.start(profile) }
                }
                Button("关闭标签") { onClose() }
                    .keyboardShortcut(.cancelAction)
            }
        }
        .padding(28)
        .frame(maxWidth: 420)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 10))
        .shadow(radius: 12)
    }

    private var statusTitle: String {
        switch tunnelInfo.status {
        case .failed: return "连接失败"
        case .stopped: return "隧道已停止"
        case .retrying: return "正在重连"
        case .connecting, .idle: return "正在连接"
        case .connected: return "已连接"
        }
    }

    private var statusSymbol: String {
        switch tunnelInfo.status {
        case .failed: return "exclamationmark.triangle"
        case .stopped: return "stop.circle"
        case .retrying: return "arrow.triangle.2.circlepath"
        case .connecting, .idle: return "ellipsis.circle"
        case .connected: return "checkmark.circle"
        }
    }

    private var statusColor: Color {
        switch tunnelInfo.status {
        case .failed: return .red
        case .stopped: return .secondary
        case .retrying, .connecting, .idle: return .orange
        case .connected: return .green
        }
    }

    private func loadIfNeeded() {
        webSessions.load(tab: tab, profile: profile, state: tunnelInfo)
    }
}
