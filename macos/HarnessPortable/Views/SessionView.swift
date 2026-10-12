import Foundation
import Combine
import WebKit
import SwiftUI

@MainActor
final class WebSessionStore: ObservableObject {
    @Published private(set) var navigationErrors: [UUID: String] = [:]
    @Published private(set) var authRequired: [UUID: Bool] = [:]
    private var webViews: [UUID: WKWebView] = [:]
    private var loadedURLs: [UUID: String] = [:]
    private var navigationDelegates: [UUID: WebSessionNavigationDelegate] = [:]
    private var activeNavigations: [UUID: WKNavigation] = [:]
    private var loadGenerations: [UUID: Int] = [:]
    private var retryWorkItems: [UUID: DispatchWorkItem] = [:]
    private var retryCounts: [UUID: Int] = [:]
    private let maximumAutomaticRetries = 3
    /// Per-tab web view state recorded in a layout preset. Seeds the tab's
    /// first navigation; what the page currently shows always wins afterwards.
    private var initialWebStates: [UUID: LayoutWebState] = [:]
    private var webStateReapplied: Set<UUID> = []
    private var lastLocalPorts: [UUID: Int] = [:]
    /// Auth mode lookup for direct tabs (keyed by direct URL); configured by
    /// the workspace from app settings so the store stays UI-agnostic.
    var directAuthMode: (String) -> String = { _ in LocalDshAuth.modeNone }

    /// The page's current URL, used to capture the live view state when a
    /// layout preset is saved.
    func currentURL(forTabID tabID: UUID) -> String? {
        webViews[tabID]?.url?.absoluteString
    }

    /// Records the preset's view state for a tab restored from a layout.
    func setInitialWebState(_ state: LayoutWebState?, forTabID tabID: UUID) {
        if let state {
            initialWebStates[tabID] = state
        } else {
            initialWebStates.removeValue(forKey: tabID)
        }
        webStateReapplied.remove(tabID)
    }

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
        var destination: String?
        switch tab.kind {
        case .management:
            destination = nil
        case .direct:
            // Preserve the origin used for TLS, Host routing and cookies.
            destination = tab.url
            // Optional auto-login for a dsh web running on this Mac under the
            // Launch Agent: graft the newest token from its stdout log onto
            // the destination. No SSH hop is involved for a direct tab, so
            // the tunnel modes (nssm/launchd-over-ssh) do not apply.
            if let url = tab.url, directAuthMode(url) == LocalDshAuth.modeLaunchd,
               let authenticated = LocalDshAuth.authTarget(
                   destination: destination ?? url,
                   logText: LocalDshAuth.currentLogText()
               ) {
                destination = authenticated
            }
        case .tunnel:
            guard state.status == .connected, state.localPort > 0 else { return }
            // Prefer the token URL fetched by the tunnel (NSSM mode): it
            // both logs in fresh sessions and revalidates silently when the
            // cookie is still good (server answers a harmless 303).
            destination = NssmAuthUrl.rewriteToLocal(
                state.authUrl, localPort: state.localPort, expectedRemotePort: 0
            ) ?? "http://127.0.0.1:\(state.localPort)"
        }
        guard var destination else { return }

        // Re-attach the dsh view state (session, sidebar, panel widths) to the
        // navigation target. What the page currently shows wins over what the
        // preset recorded, so reloading or reconnecting never throws the user
        // back to a session they have since switched away from; the recorded
        // state is the starting point for the tab's first navigation.
        let webState = ViewStateUrls.extract(currentURL(forTabID: tab.id)) ?? initialWebStates[tab.id]
        destination = ViewStateUrls.merge(destination, state: webState)
        guard let url = URL(string: destination) else { return }
        trackLocalPort(forTabID: tab.id, in: destination)

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
        authRequired.removeValue(forKey: tabID)
        initialWebStates.removeValue(forKey: tabID)
        webStateReapplied.remove(tabID)
        lastLocalPorts.removeValue(forKey: tabID)
    }

    private func beginLoad(for tabID: UUID) {
        retryWorkItems[tabID]?.cancel()
        retryWorkItems.removeValue(forKey: tabID)
        retryCounts[tabID] = 0
        authRequired[tabID] = false
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
        checkAuthRequired(tabID: tabID, webView: webView)
        reapplyWebStateSoon(tabID: tabID, webView: webView)
    }

    /// Remembers the tunnel's live local port so the one-shot state re-apply
    /// can navigate back to the tab's clean dsh root.
    private func trackLocalPort(forTabID tabID: UUID, in destination: String) {
        guard let components = URLComponents(string: destination),
              components.scheme?.lowercased() == "http",
              components.host == "127.0.0.1" || components.host == "localhost",
              let port = components.port else { return }
        lastLocalPorts[tabID] = port
    }

    /// Puts a preset's recorded view state back after the page has settled.
    ///
    /// dsh's startup URL carries a one-shot token and answers with a redirect
    /// to the clean root, and that redirect drops the query string: state
    /// merged into the token URL never reaches the page, while the plugin
    /// reads its parameters at boot only. So give the page a moment to mirror
    /// whatever state it already has, and if ours never arrived, navigate once
    /// to the clean root carrying it. The auth hand-off is left alone — its
    /// own completion re-enters here.
    private func reapplyWebStateSoon(tabID: UUID, webView: WKWebView) {
        guard initialWebStates[tabID] != nil, !webStateReapplied.contains(tabID) else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) { [weak self, weak webView] in
            guard let self, let webView, self.webViews[tabID] === webView else { return }
            guard self.initialWebStates[tabID] != nil, !self.webStateReapplied.contains(tabID) else { return }
            let live = webView.url?.absoluteString
            if let live, live.range(of: "token=", options: .caseInsensitive) != nil {
                // The token hand-off is still in flight; its completion calls us again.
                return
            }
            self.webStateReapplied.insert(tabID)
            guard let live, ViewStateUrls.needsReapply(live),
                  let root = self.cleanRootURL(forTabID: tabID) else { return }
            let state = ViewStateUrls.extract(live) ?? self.initialWebStates[tabID]
            let target = ViewStateUrls.merge(root, state: state)
            guard let url = URL(string: target) else { return }
            self.beginLoad(for: tabID)
            self.navigationErrors.removeValue(forKey: tabID)
            self.loadedURLs[tabID] = target
            self.activeNavigations[tabID] = webView.load(
                URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 30)
            )
        }
    }

    /// The tab's own dsh root without a query string, used for the one re-apply.
    private func cleanRootURL(forTabID tabID: UUID) -> String? {
        if let port = lastLocalPorts[tabID] {
            return "http://127.0.0.1:\(port)"
        }
        guard let target = loadedURLs[tabID],
              var components = URLComponents(string: target) else { return nil }
        components.query = nil
        components.fragment = nil
        return components.url?.absoluteString
    }

    /// dsh-web style services print a one-time token URL on the server; the
    /// bare host:port answers 401 until that URL has been opened once.
    /// Inspect the loaded body and surface the manual paste sheet when the
    /// rejection page is served (the tunnel usually fetches the URL
    /// automatically in NSSM mode — this is the fallback).
    private func checkAuthRequired(tabID: UUID, webView: WKWebView) {
        guard let current = webView.url?.absoluteString, current.hasPrefix("http") else { return }
        webView.evaluateJavaScript(
            "(document.body ? (document.body.innerText || '') : '').slice(0, 4000)"
        ) { [weak self] result, _ in
            DispatchQueue.main.async {
                guard let self,
                      let text = result as? String,
                      NssmAuthUrl.looksLikeAuthRequired(text) else { return }
                self.authRequired[tabID] = true
            }
        }
    }

    /// Opens the pasted token URL (rewritten onto the current base). Returns
    /// false when the input could not be interpreted, so the caller can show
    /// a hint instead of dismissing the sheet.
    @discardableResult
    func openAuth(tabID: UUID, input: String) -> Bool {
        guard let base = loadedURLs[tabID],
              let target = NssmAuthUrl.buildAuthTarget(base: base, input: input),
              let url = URL(string: target) else {
            return false
        }
        authRequired[tabID] = false
        beginLoad(for: tabID)
        navigationErrors.removeValue(forKey: tabID)
        let webView = webView(for: tabID)
        activeNavigations[tabID] = webView.load(
            URLRequest(url: url, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 30)
        )
        return true
    }

    func dismissAuth(tabID: UUID) {
        authRequired[tabID] = false
    }

    private func navigationFailed(
        tabID: UUID,
        webView: WKWebView,
        navigation: WKNavigation?,
        message: String
    ) {
        guard webViews[tabID] === webView else { return }
        if let navigation,
           let activeNavigation = activeNavigations[tabID],
           activeNavigation !== navigation {
            return
        }
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

    @State private var authInput = ""
    @State private var authHint: String?

    @Environment(\.colorScheme) var colorScheme

    private var dsh: DshPalette { Dsh.palette(colorScheme) }

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
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(dsh.bgBase)

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
            .background(dsh.bgLayer2, in: RoundedRectangle(cornerRadius: Dsh.radiusControl))
            .overlay(
                RoundedRectangle(cornerRadius: Dsh.radiusControl)
                    .stroke(dsh.borderL1, lineWidth: Dsh.borderWidth)
            )
            .padding(8)

            if tab.kind == .tunnel && tunnelInfo.status != .connected {
                tunnelOverlay
            }
            if webSessions.authRequired[tab.id] == true {
                authPromptOverlay
            }
            if let error = webSessions.navigationErrors[tab.id] {
                navigationErrorOverlay(error)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onAppear {
            DispatchQueue.main.async {
                loadIfNeeded()
            }
        }
        .onChange(of: tunnelInfo.status) { _ in
            DispatchQueue.main.async {
                loadIfNeeded()
            }
        }
        .onChange(of: tunnelInfo.localPort) { _ in
            DispatchQueue.main.async {
                loadIfNeeded()
            }
        }
        .onChange(of: tunnelInfo.authUrl) { _ in
            DispatchQueue.main.async {
                loadIfNeeded()
            }
        }
        .onChange(of: tunnelInfo.generation) { _ in
            DispatchQueue.main.async {
                loadIfNeeded()
            }
        }
    }

    @ViewBuilder
    private var authPromptOverlay: some View {
        VStack(spacing: 12) {
            Image(systemName: "lock.shield")
                .font(.system(size: 28, weight: .medium))
                .foregroundStyle(dsh.warning)
            Text("网页要求重新认证")
                .font(.headline)
                .foregroundStyle(dsh.textPrimary)
            Text(
                "服务端要求先用带令牌的 URL 打开一次（例如 dsh 更新后）。"
                    + "请复制服务器上 dsh web 打印的完整 URL 粘贴到下面，"
                    + "也可直接粘贴 token 本身，将直接在内置浏览器中完成认证。"
            )
            .font(.callout)
            .foregroundStyle(dsh.textSecondary)
            .multilineTextAlignment(.center)
            TextField("完整 URL、?token=… 或 token", text: $authInput)
                .dshInput()
                .frame(maxWidth: 420)
                .onSubmit(openPastedAuthUrl)
            if let authHint {
                Text(authHint)
                    .font(.caption)
                    .foregroundStyle(dsh.danger)
            }
            HStack {
                Button("打开", action: openPastedAuthUrl)
                    .keyboardShortcut(.defaultAction)
                    .buttonStyle(DshPrimaryButtonStyle())
                Button("取消") {
                    authHint = nil
                    webSessions.dismissAuth(tabID: tab.id)
                }
                .keyboardShortcut(.cancelAction)
            }
        }
        .padding(28)
        .frame(maxWidth: 500)
        .background(dsh.bgLayer1, in: RoundedRectangle(cornerRadius: Dsh.radiusCard))
        .overlay(
            RoundedRectangle(cornerRadius: Dsh.radiusCard)
                .stroke(dsh.borderL1, lineWidth: Dsh.borderWidth)
        )
    }

    private func openPastedAuthUrl() {
        if !webSessions.openAuth(tabID: tab.id, input: authInput) {
            authHint = "无法识别输入。请粘贴 dsh web 打印的完整 URL、?token=… 查询串或 token 本身。"
        } else {
            authHint = nil
            authInput = ""
        }
    }

    @ViewBuilder
    private func navigationErrorOverlay(_ message: String) -> some View {
        VStack(spacing: 10) {
            Image(systemName: "exclamationmark.triangle")
                .font(.system(size: 28, weight: .medium))
                .foregroundStyle(dsh.danger)
            Text("页面加载失败")
                .font(.headline)
                .foregroundStyle(dsh.textPrimary)
            Text(message)
                .font(Dsh.mono(12))
                .foregroundStyle(dsh.textSecondary)
                .multilineTextAlignment(.center)
                .textSelection(.enabled)
            Button("重新加载") { webSessions.reload(tabID: tab.id) }
                .buttonStyle(DshPrimaryButtonStyle())
        }
        .padding(28)
        .frame(maxWidth: 520)
        .background(dsh.bgLayer1, in: RoundedRectangle(cornerRadius: Dsh.radiusCard))
        .overlay(
            RoundedRectangle(cornerRadius: Dsh.radiusCard)
                .stroke(dsh.borderL1, lineWidth: Dsh.borderWidth)
        )
    }

    @ViewBuilder
    private var tunnelOverlay: some View {
        VStack(spacing: 10) {
            Image(systemName: statusSymbol)
                .font(.system(size: 28, weight: .medium))
                .foregroundStyle(statusColor)
            Text(statusTitle)
                .font(.headline)
                .foregroundStyle(dsh.textPrimary)
            if let message = tunnelInfo.message, !message.isEmpty {
                Text(message)
                    .font(.callout)
                    .foregroundStyle(dsh.textSecondary)
                    .multilineTextAlignment(.center)
            }
            HStack {
                if tunnelInfo.status == .failed || tunnelInfo.status == .retrying,
                   let profile {
                    Button("重新连接") { tunnels.start(profile) }
                        .buttonStyle(DshPrimaryButtonStyle())
                }
                Button("关闭标签") { onClose() }
                    .keyboardShortcut(.cancelAction)
            }
        }
        .padding(28)
        .frame(maxWidth: 420)
        .background(dsh.bgLayer1, in: RoundedRectangle(cornerRadius: Dsh.radiusCard))
        .overlay(
            RoundedRectangle(cornerRadius: Dsh.radiusCard)
                .stroke(dsh.borderL1, lineWidth: Dsh.borderWidth)
        )
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
        case .failed: return dsh.danger
        case .stopped: return dsh.textTertiary
        case .retrying, .connecting, .idle: return dsh.warning
        case .connected: return dsh.success
        }
    }

    private func loadIfNeeded() {
        webSessions.load(tab: tab, profile: profile, state: tunnelInfo)
    }
}
