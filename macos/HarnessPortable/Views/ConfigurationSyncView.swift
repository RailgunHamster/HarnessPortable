import SwiftUI

@MainActor
struct ConfigurationSyncView: View {
    @ObservedObject var sync: ConfigurationSync
    @State private var draft: WebDavOptions
    @State private var password = ""
    @State private var clearPassword = false
    @State private var draftMessage = ""
    init(sync: ConfigurationSync) { self.sync = sync; _draft = State(initialValue: sync.options) }
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("WebDAV 配置同步").font(.headline)
            Text("三端共用链接库、收藏、分组、排序和外观；各平台界面偏好分别保存。连接密码、私钥、网页登录令牌和 WebDAV 账号不上传。")
                .font(.callout).foregroundStyle(.secondary)
            TextField("WebDAV 私有目录（请先创建）", text: $draft.url).dshInput().disabled(sync.busy)
            TextField("WebDAV 用户名", text: $draft.username).dshInput().disabled(sync.busy)
            SecureField(sync.hasPassword ? "WebDAV 密码 · 已保存，留空保留" : "WebDAV 密码", text: $password).dshInput().disabled(sync.busy)
            Toggle("清除本机 WebDAV 密码", isOn: $clearPassword).disabled(sync.busy)
            Toggle("自动同步：启动、回到前台、修改配置及前台每 5 分钟", isOn: $draft.autoSync).disabled(sync.busy)
            Toggle("允许可信局域网明文 HTTP（凭据会明文传输）", isOn: $draft.allowHttp).disabled(sync.busy)
            Text("请使用私有目录。同步文件含地址和账号名，不含凭据；直连地址中的登录参数只留在本机。")
                .font(.callout).foregroundStyle(.secondary)
            HStack {
                Button("保存同步设置") {
                    do { try sync.configure(draft, password: password.isEmpty ? nil : password, clearPassword: clearPassword); password = ""; clearPassword = false; draftMessage = "同步设置已保存" }
                    catch { draftMessage = error.localizedDescription }
                }.buttonStyle(DshPrimaryButtonStyle()).disabled(sync.busy)
                Button("撤销") { draft = sync.options; password = ""; clearPassword = false; draftMessage = "已撤销草稿" }.disabled(sync.busy)
            }
            if !draftMessage.isEmpty { Text(draftMessage).font(.callout).foregroundStyle(.secondary) }
            Divider()
            Text(sync.message).font(.callout).textSelection(.enabled)
            if let time = sync.lastSuccess { Text("上次成功：\(time.formatted(date: .numeric, time: .standard))").font(.caption).foregroundStyle(.secondary) }
            if sync.busy { ProgressView() }
            HStack {
                Button("测试目录") { Task { await sync.test() } }.disabled(sync.busy || sync.options.url.isEmpty)
                Button("立即同步") { Task { await sync.run() } }.disabled(sync.busy || sync.options.url.isEmpty)
                if sync.busy { Button("取消") { sync.cancel() } }
            }
            if sync.phase == "conflict" {
                Text("\(sync.conflicts.count) 项冲突 · 仅选择冲突项保留哪一端，其余修改仍合并").font(.callout)
                Text(sync.conflicts.prefix(15).joined(separator: "\n")).font(.caption).textSelection(.enabled)
                HStack {
                    Button("冲突项保留本机") { Task { await sync.run(choice: "local") } }
                    Button("冲突项保留云端") { Task { await sync.run(choice: "remote") } }
                }
            }
        }
    }
}
