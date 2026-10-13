import SwiftUI
import AppKit
import UniformTypeIdentifiers

@MainActor
struct SettingsWindowView: View {
    @ObservedObject var settings: SettingsStore
    @ObservedObject var profiles: ProfileStore
    @ObservedObject var updater: UpdateService
    @Environment(\.colorScheme) private var colorScheme
    @Environment(\.openWindow) private var openWindow
    @State private var category = "general"
    @State private var draftSource: String
    @State private var draftHome: String
    @State private var draftGithub: String
    @State private var draftProxy: UpdateProxy
    @State private var proxyStatus = ""
    @State private var status = "偏好立即保存；地址修改需要明确保存。"
    private var dsh: DshPalette { Dsh.palette(colorScheme) }
    init(settings: SettingsStore, profiles: ProfileStore, updater: UpdateService) {
        self.settings = settings; self.profiles = profiles; self.updater = updater
        _draftSource = State(initialValue: settings.value.updateSourceSelected)
        _draftHome = State(initialValue: settings.value.updateHomeUrl)
        _draftGithub = State(initialValue: settings.value.updateGithubUrl)
        _draftProxy = State(initialValue: UpdateProxy(settings: settings.value))
    }
    var body: some View {
        VStack(spacing: 0) {
            HStack {
                VStack(alignment: .leading, spacing: 5) { Text("设置").font(.title2); Text("本设备 · 网页与工作区保持打开").font(.callout).foregroundStyle(dsh.textSecondary) }
                Spacer()
            }.padding(22).background(dsh.bgLayer1)
            Divider()
            HStack(spacing: 0) {
                VStack(alignment: .leading, spacing: 8) {
                    categoryButton("通用", value: "general")
                    categoryButton("工作区", value: "workspace")
                    categoryButton("数据与安全", value: "security")
                    categoryButton("更新与关于", value: "updates")
                    Spacer()
                }.padding(14).frame(width: 155).background(dsh.bgLayer1)
                ScrollView {
                    VStack(alignment: .leading, spacing: 20) {
                        if category == "general" {
                            Text("通用").font(.headline)
                            Picker("外观", selection: preference(\.appearance)) { Text("跟随系统").tag("system"); Text("浅色").tag("light"); Text("深色").tag("dark") }
                            Picker("关闭主窗口时", selection: preference(\.closeBehavior)) { Text("退出并断开连接").tag("exit"); Text("保持后台运行").tag("tray") }
                            Text("关闭网页标签只关闭此网页。SSH 连接可以在连接库中单独断开。")
                                .font(.callout).foregroundStyle(dsh.textSecondary)
                        } else if category == "workspace" {
                            Text("工作区").font(.headline)
                            Toggle("启动时恢复上次工作区", isOn: preference(\.restoreLastLayoutOnStartup))
                            Text("自动记录标签、网页状态和分屏比例。").font(.callout).foregroundStyle(dsh.textSecondary)
                            Toggle("恢复时自动连接服务器", isOn: preference(\.reconnectRestoredWorkspace))
                            Text("关闭时仅恢复布局，打开服务后再连接。").font(.callout).foregroundStyle(dsh.textSecondary)
                            Divider()
                            Text("命名预设").font(.headline)
                            Text("工具栏中的“保存预设”用于保存一套固定布局，与上次工作区的自动快照分别保留。").font(.callout).foregroundStyle(dsh.textSecondary)
                        } else if category == "security" {
                            Text("数据与安全").font(.headline)
                            Text("从连接库菜单编辑对应服务器。SSH 登录与网页登录分别配置，修改下次连接生效。").foregroundStyle(dsh.textSecondary)
                            Button("打开连接库") { settings.update { $0.connectionSidebarCollapsed = false }; openWindow(id: "main") }
                            Divider()
                            Text("导出连接配置").font(.headline)
                            Text("只导出服务器和端口等连接信息，不包含密码、私钥内容和服务令牌。").font(.callout).foregroundStyle(dsh.textSecondary)
                            Button("导出配置…") { exportConfig() }
                        } else {
                            Text("更新与关于").font(.headline)
                            Text("Harness Portable · \(UpdateService.currentVersion)").font(Dsh.mono(12))
                            Text("正在使用：\(settings.value.updateSourceSelected == "github" ? "GitHub" : "局域网目录")").foregroundStyle(dsh.textSecondary)
                            Button("检查更新") { updater.check() }.buttonStyle(DshPrimaryButtonStyle()).disabled(busy)
                            Text(updateStatus).font(.callout).foregroundStyle(dsh.textSecondary)
                            if case .available = updater.phase { Button("下载并安装") { updater.download() }.buttonStyle(DshPrimaryButtonStyle()) }
                            if case .readyToInstall = updater.phase { Button("打开安装镜像") { updater.openInstaller() } }
                            if case .downloading(let fraction) = updater.phase { ProgressView(value: fraction) }
                            Divider()
                            Toggle("更新使用代理", isOn: Binding(get: { draftProxy.enabled }, set: { draftProxy.enabled = $0; proxyStatus = "修改后请保存代理设置" })).disabled(busy)
                            Text("仅用于检查更新和下载安装包 · 默认关闭").font(.callout).foregroundStyle(dsh.textSecondary)
                            TextField("HTTP 代理地址，例如 localhost:7890", text: Binding(get: { draftProxy.address }, set: { draftProxy.address = $0; proxyStatus = "修改后请保存代理设置" })).dshInput().disabled(!draftProxy.enabled || busy)
                            Text(proxyStatus.isEmpty ? (settings.value.updateProxyEnabled ? "已启用 · \(settings.value.updateProxyAddress)" : "已关闭 · 更新直接连接") : proxyStatus).font(.callout).foregroundStyle(dsh.textSecondary)
                            HStack {
                                Button("保存代理设置") { saveProxy() }.disabled(busy)
                                Button("撤销") { draftProxy = UpdateProxy(settings: settings.value); proxyStatus = "" }.disabled(busy)
                            }
                            DisclosureGroup("高级：局域网目录或自定义更新源") {
                                VStack(alignment: .leading, spacing: 12) {
                                    Picker("更新源", selection: $draftSource) { Text("GitHub").tag("github"); Text("局域网目录 / HTTP").tag("home") }
                                    if draftSource == "github" { TextField("GitHub 发布下载基址", text: $draftGithub).dshInput() }
                                    else { TextField("smb://、已挂载目录或 HTTP 地址", text: $draftHome).dshInput() }
                                    Text("切换或检查不会保存草稿。").font(.callout).foregroundStyle(dsh.textSecondary)
                                    HStack { Button("保存并使用") { saveSource() }.disabled(busy); Button("撤销") { resetDraft() }; Button("官方源") { useOfficialSource() }.disabled(busy) }
                                }.padding(.top, 12)
                            }
                            if !updater.notes.isEmpty { DisclosureGroup("版本说明") { Text(updater.notes).textSelection(.enabled).padding(.top, 10) } }
                        }
                    }.frame(maxWidth: .infinity, alignment: .leading).padding(24)
                }
            }
            Divider()
            Text(status).font(.callout).foregroundStyle(dsh.textSecondary).frame(maxWidth: .infinity, alignment: .leading).padding(15)
        }
        .foregroundStyle(dsh.textPrimary).background(dsh.bgBase).frame(minWidth: 680, minHeight: 540)
        .preferredColorScheme(settings.value.appearance == "dark" ? .dark : settings.value.appearance == "light" ? .light : nil)
    }
    private func categoryButton(_ title: String, value: String) -> some View {
        Button { category = value } label: { Text(title).frame(maxWidth: .infinity, alignment: .leading).padding(10) }
            .buttonStyle(.plain).background(category == value ? dsh.accentSoft : dsh.bgLayer1, in: RoundedRectangle(cornerRadius: 8))
    }
    private func preference<Value>(_ key: WritableKeyPath<AppSettings, Value>) -> Binding<Value> {
        Binding(get: { settings.value[keyPath: key] }, set: { value in settings.update { $0[keyPath: key] = value }; status = "已保存 · 本设备偏好立即生效" })
    }
    private var busy: Bool { switch updater.phase { case .checking, .downloading: return true; default: return false } }
    private var updateStatus: String {
        switch updater.phase {
        case .idle: return "尚未检查更新"
        case .checking: return "正在检查更新…"
        case .upToDate: return "当前已是最新版本"
        case .available(let version): return "发现新版本 \(version)"
        case .downloading: return "正在下载更新…"
        case .readyToInstall(let version): return "版本 \(version) 已就绪"
        case .failed(let message): return message
        }
    }
    private func saveSource() {
        let value = (draftSource == "github" ? draftGithub : draftHome).trimmingCharacters(in: .whitespacesAndNewlines)
        let url = URL(string: value)
        let valid = draftSource == "github" ? url?.host == "github.com" && ["http", "https"].contains(url?.scheme ?? "")
            : value.hasPrefix("/") || ["smb", "http", "https"].contains(url?.scheme ?? "")
        guard !value.isEmpty, valid else { status = "地址未保存：请输入完整地址或绝对目录路径。"; return }
        settings.update { $0.updateSourceSelected = draftSource; $0.updateHomeUrl = draftHome.trimmingCharacters(in: .whitespacesAndNewlines); $0.updateGithubUrl = draftGithub.trimmingCharacters(in: .whitespacesAndNewlines) }
        updater.resetSourceResult(); status = "已保存 · 下次检查使用此更新源"
    }
    private func saveProxy() {
        guard !busy else { return }
        let next = UpdateProxy(enabled: draftProxy.enabled, address: draftProxy.address)
        guard !next.enabled || UpdateProxy.endpoint(next.address) != nil else {
            proxyStatus = "请输入 HTTP 代理地址，例如 localhost:7890。"; return
        }
        settings.update { $0.updateProxyEnabled = next.enabled; $0.updateProxyAddress = next.address }
        draftProxy = next; updater.resetSourceResult()
        proxyStatus = "已保存 · \(next.enabled ? "代理已启用" : "代理已关闭")"
        status = "已保存代理设置 · 下次检查与下载生效"
    }
    private func resetDraft() { draftSource = settings.value.updateSourceSelected; draftHome = settings.value.updateHomeUrl; draftGithub = settings.value.updateGithubUrl; status = "已撤销地址修改" }
    private func useOfficialSource() {
        settings.update { $0.updateSourceSelected = "github"; $0.updateGithubUrl = "https://github.com/RailgunHamster/HarnessPortable/releases/latest/download" }
        updater.resetSourceResult(); resetDraft(); status = "已保存 · 使用官方 GitHub 更新源"
    }
    private func exportConfig() {
        let panel = NSSavePanel(); panel.allowedContentTypes = [.json]; panel.nameFieldStringValue = "harness-connections.json"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]; try encoder.encode(ProfileConfig(version: 1, tunnels: profiles.tunnels, directs: profiles.directs)).write(to: url); status = "配置已导出 · 不包含凭据" }
        catch { status = "导出失败：\(error.localizedDescription)" }
    }
}
