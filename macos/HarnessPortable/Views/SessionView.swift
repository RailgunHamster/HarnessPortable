import Foundation
import Combine
import WebKit
import SwiftUI

@MainActor
final class WebSessionStore: ObservableObject {
    @Published private(set) var navigationErrors: [UUID: String] = [:]
    private var webViews: [UUID: WKWebView] = [:]
    private var loadedURLs: [UUID: String] = [:]
    private var navigationDelegates: [UUID: WebSessionNavigationDelegate] = [:]
    private var activeNavigations: [UUID: WKNavigation] = [:]
    private var loadGenerations: [UUID: Int] = [:]
    private var retryWorkItems: [UUID: DispatchWorkItem] = [:]
    private var retryCounts: [UUID: Int] = [:]
    private let maximumAutomaticRetries = 3

    func webView(for tabID: UUID) -> WKWebView {
        if let existing = webViews[tabID] { return existing }
        let configuration = WKWebViewConfiguration()
        configuration.websiteDataStore = .default()
        configuration.defaultWebpagePreferences.allowsContentJavaScript = true
        configuration.preferences.javaScriptCanOpenWindowsAutomatically = true
        let webView = WKWebView(frame: .zero, configuration: configuration)
        webView.allowsBackForwardNavigationGestures = true
        let delegate = WebSessionNavigationDelegate(
            onStart: { [weak self] webView, navigation in
                DispatchQueue.main.async { [weak self, weak webView] in
                    guard let webView else { return }
                    self?.navigationStarted(tabID: tabID, webView: webView, navigation: navigation)
                }
            },
            onSuccess: { [weak self] webView, navigation in
                DispatchQueue.main.async { [weak self, weak webView] in
                    guard let webView else { return }
                    self?.navigationSucceeded(tabID: tabID, webView: webView, navigation: navigation)
                }
            },
            onFailure: { [weak self] webView, navigation, message in
                DispatchQueue.main.async { [weak self, weak webView] in
                    guard let webView else { return }
                    self?.navigationFailed(tabID: tabID, webView: webView, navigation: navigation, message: message)
                }
            }
        )
        webView.navigationDelegate = delegate
        webView.uiDelegate = delegate
        navigationDelegates[tabID] = delegate
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
            guard state.status == .connected, state.localPort > 0 else { return }
            destination = "http://127.0.0.1:\(state.localPort)"
        }
        guard let destination, let url = URL(string: destination) else { return }

        beginLoad(for: tab.id)
        navigationErrors.removeValue(forKey: tab.id)

        let shouldReload = loadedURLs[tab.id] == destination
        loadedURLs[tab.id] = destination
        let webView = webView(for: tab.id)
        if shouldReload {
            activeNavigations[tab.id] = webView.reload()
        } else {
            let cachePolicy: URLRequest.CachePolicy = tab.kind == .tunnel
                ? .reloadIgnoringLocalCacheData
                : .useProtocolCachePolicy
            activeNavigations[tab.id] = webView.load(
                URLRequest(url: url, cachePolicy: cachePolicy, timeoutInterval: 30)
            )
        }
    }

    func reload(tabID: UUID) {
        beginLoad(for: tabID)
        navigationErrors.removeValue(forKey: tabID)
        if let webView = webViews[tabID] {
            activeNavigations[tabID] = webView.reload()
        }
    }

    func remove(tabID: UUID) {
        retryWorkItems[tabID]?.cancel()
        retryWorkItems.removeValue(forKey: tabID)
        retryCounts.removeValue(forKey: tabID)
        if let webView = webViews[tabID] {
            webView.navigationDelegate = nil
            webView.stopLoading()
        }
        webViews.removeValue(forKey: tabID)
        activeNavigations.removeValue(forKey: tabID)
        loadGenerations.removeValue(forKey: tabID)
        navigationDelegates.removeValue(forKey: tabID)
        navigationErrors.removeValue(forKey: tabID)
        loadedURLs.removeValue(forKey: tabID)
    }

    private func beginLoad(for tabID: UUID) {
        retryWorkItems[tabID]?.cancel()
        retryWorkItems.removeValue(forKey: tabID)
        retryCounts[tabID] = 0
        loadGenerations[tabID] = (loadGenerations[tabID] ?? 0) + 1
    }

    private func navigationStarted(tabID: UUID, webView: WKWebView, navigation: WKNavigation?) {
        guard webViews[tabID] === webView,
              activeNavigations[tabID] == nil,
              let navigation else { return }
        activeNavigations[tabID] = navigation
    }

    private func navigationSucceeded(tabID: UUID, webView: WKWebView, navigation: WKNavigation?) {
        guard webViews[tabID] === webView,
              let navigation,
              activeNavigations[tabID] === navigation else { return }
        activeNavigations.removeValue(forKey: tabID)
        retryWorkItems[tabID]?.cancel()
        retryWorkItems.removeValue(forKey: tabID)
        retryCounts.removeValue(forKey: tabID)
        navigationErrors.removeValue(forKey: tabID)
    }

    private func navigationFailed(
        tabID: UUID,
        webView: WKWebView,
        navigation: WKNavigation?,
        message: String
    ) {
        guard webViews[tabID] === webView,
              let navigation,
              activeNavigations[tabID] === navigation else { return }
        activeNavigations.removeValue(forKey: tabID)
        navigationErrors[tabID] = message

        let attempt = retryCounts[tabID] ?? 0
        guard attempt < maximumAutomaticRetries else { return }
        retryCounts[tabID] = attempt + 1
        let loadGeneration = loadGenerations[tabID] ?? 0

        retryWorkItems[tabID]?.cancel()
        let retry = DispatchWorkItem { [weak self, weak webView] in
            guard let self, let webView,
                  self.webViews[tabID] === webView,
                  self.loadGenerations[tabID] == loadGeneration else { return }
            self.retryWorkItems.removeValue(forKey: tabID)
            self.activeNavigations[tabID] = webView.reload()
        }
        retryWorkItems[tabID] = retry
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.35, execute: retry)
    }
}

private final class WebSessionNavigationDelegate: NSObject, WKNavigationDelegate, WKUIDelegate {
    let onStart: (WKWebView, WKNavigation?) -> Void
    let onSuccess: (WKWebView, WKNavigation?) -> Void
    let onFailure: (WKWebView, WKNavigation?, String) -> Void

    init(
        onStart: @escaping (WKWebView, WKNavigation?) -> Void,
        onSuccess: @escaping (WKWebView, WKNavigation?) -> Void,
        onFailure: @escaping (WKWebView, WKNavigation?, String) -> Void
    ) {
        self.onStart = onStart
        self.onSuccess = onSuccess
        self.onFailure = onFailure
    }

    func webView(
        _ webView: WKWebView,
        createWebViewWith configuration: WKWebViewConfiguration,
        for navigationAction: WKNavigationAction,
        windowFeatures: WKWindowFeatures
    ) -> WKWebView? {
        guard navigationAction.targetFrame == nil else { return nil }
        webView.load(navigationAction.request)
        return nil
    }

    func webView(_ webView: WKWebView, didStartProvisionalNavigation navigation: WKNavigation!) {
        onStart(webView, navigation)
    }

    func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
        onSuccess(webView, navigation)
    }

    func webView(_ webView: WKWebView, didFail navigation: WKNavigation?, withError error: Error) {
        reportFailure(for: webView, navigation: navigation, error: error)
    }

    func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation?, withError error: Error) {
        reportFailure(for: webView, navigation: navigation, error: error)
    }

    private func reportFailure(for webView: WKWebView, navigation: WKNavigation?, error: Error) {
        guard (error as NSError).code != NSURLErrorCancelled else { return }
        onFailure(webView, navigation, error.localizedDescription)
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
            if let error = webSessions.navigationErrors[tab.id] {
                navigationErrorOverlay(error)
            }
        }
        .onAppear { loadIfNeeded() }
        .onChange(of: tunnelInfo.status) { _ in loadIfNeeded() }
        .onChange(of: tunnelInfo.localPort) { _ in loadIfNeeded() }
    }

    @ViewBuilder
    private func navigationErrorOverlay(_ message: String) -> some View {
        VStack(spacing: 10) {
            Image(systemName: "exclamationmark.triangle")
                .font(.system(size: 28, weight: .medium))
                .foregroundStyle(.red)
            Text("页面加载失败")
                .font(.headline)
            Text(message)
                .font(.callout)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .textSelection(.enabled)
            Button("重新加载") { webSessions.reload(tabID: tab.id) }
        }
        .padding(28)
        .frame(maxWidth: 520)
        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 10))
        .shadow(radius: 12)
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
