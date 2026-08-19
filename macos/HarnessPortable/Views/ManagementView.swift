import SwiftUI

@MainActor
struct ManagementView: View {
    @ObservedObject var profiles: ProfileStore
    @ObservedObject var tunnels: TunnelManager
    @ObservedObject var settings: SettingsStore
    let onConnect: (TunnelProfile) -> Void
    let onStop: (String) -> Void
    let onOpenDirect: (String) -> Void
    let onDelete: (TunnelProfile) -> Void

    @State private var editingProfile: TunnelProfile?
    @State private var directInput = ""

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                sectionHeader("SSH 隧道", systemImage: "point.3.connected.trianglepath.dotted") {
                    Button {
                        editingProfile = TunnelProfile()
                    } label: {
                        Label("添加", systemImage: "plus")
                    }
                }

                if profiles.tunnels.isEmpty {
                    Text("还没有保存的隧道")
                        .foregroundStyle(.secondary)
                        .padding(.vertical, 8)
                } else {
                    VStack(spacing: 0) {
                        ForEach(profiles.tunnels) { profile in
                            tunnelRow(profile)
                            if profile.id != profiles.tunnels.last!.id { Divider() }
                        }
                    }
                    .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 8))
                }

                sectionHeader("直连地址", systemImage: "globe") {
                    HStack(spacing: 6) {
                        TextField("主机或 URL", text: $directInput)
                            .textFieldStyle(.roundedBorder)
                            .frame(width: 240)
                        Button {
                            guard let url = profiles.addDirect(directInput) else { return }
                            directInput = ""
                            onOpenDirect(url)
                        } label: {
                            Image(systemName: "plus")
                        }
                        .help("添加直连")
                        .disabled(directInput.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    }
                }

                if profiles.directs.isEmpty {
                    Text("还没有保存的直连地址")
                        .foregroundStyle(.secondary)
                        .padding(.vertical, 8)
                } else {
                    VStack(spacing: 0) {
                        ForEach(profiles.directs, id: \.self) { url in
                            HStack(spacing: 10) {
                                Image(systemName: "link")
                                    .foregroundStyle(.secondary)
                                Text(url)
                                    .lineLimit(1)
                                Spacer()
                                Button {
                                    onOpenDirect(url)
                                } label: {
                                    Image(systemName: "arrow.up.right.square")
                                }
                                .help("打开直连")
                                .buttonStyle(.borderless)
                                Button {
                                    profiles.deleteDirect(url)
                                } label: {
                                    Image(systemName: "trash")
                                }
                                .help("删除")
                                .buttonStyle(.borderless)
                            }
                            .padding(.horizontal, 12)
                            .padding(.vertical, 9)
                            if url != profiles.directs.last! { Divider() }
                        }
                    }
                    .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 8))
                }

                sectionHeader("设置", systemImage: "gearshape")
                VStack(alignment: .leading, spacing: 12) {
                    Picker("关闭主窗口", selection: closeBehaviorBinding) {
                        Text("直接退出").tag("exit")
                        Text("保持在菜单栏").tag("tray")
                    }
                    .pickerStyle(.menu)
                    Toggle("启动时恢复上次布局", isOn: restoreLayoutBinding)
                }
                .padding(14)
                .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 8))
            }
            .padding(24)
            .frame(maxWidth: 900, alignment: .leading)
        }
        .sheet(item: $editingProfile) { profile in
            ProfileEditorView(profile: profile) { saved in
                profiles.upsertTunnel(saved)
                editingProfile = nil
            }
        }
    }

    @ViewBuilder
    private func tunnelRow(_ profile: TunnelProfile) -> some View {
        let state = tunnels.state(for: profile.id)
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 3) {
                Text(profile.displayName)
                    .font(.headline)
                Text(profile.summary)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                if let message = state.message, state.status != .idle {
                    Text(message)
                        .font(.caption2)
                        .foregroundStyle(state.status == .failed ? Color.red : Color.secondary)
                        .lineLimit(1)
                }
            }
            Spacer()
            statusBadge(state)
            if state.status == .connected || state.status == .connecting || state.status == .retrying {
                Button {
                    onStop(profile.id)
                } label: {
                    Image(systemName: "stop.fill")
                }
                .help("停止隧道")
            } else {
                Button {
                    onConnect(profile)
                } label: {
                    Image(systemName: "play.fill")
                }
                .help("连接隧道")
            }
            Button {
                editingProfile = profile
            } label: {
                Image(systemName: "pencil")
            }
            .help("编辑")
            Button {
                onDelete(profile)
            } label: {
                Image(systemName: "trash")
            }
            .help("删除")
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 11)
    }

    @ViewBuilder
    private func statusBadge(_ state: TunnelInfo) -> some View {
        switch state.status {
        case .connected:
            Label(state.localPort > 0 ? "已连接 \(state.localPort)" : "已连接", systemImage: "checkmark.circle.fill")
                .foregroundStyle(.green)
        case .connecting:
            Label("连接中", systemImage: "ellipsis.circle")
                .foregroundStyle(.orange)
        case .retrying:
            Label("重连中", systemImage: "arrow.triangle.2.circlepath")
                .foregroundStyle(.orange)
        case .failed:
            Label("失败", systemImage: "xmark.circle.fill")
                .foregroundStyle(.red)
        case .stopped, .idle:
            EmptyView()
        }
    }

    private func sectionHeader(_ title: String, systemImage: String) -> some View {
        HStack {
            Label(title, systemImage: systemImage)
                .font(.title3.weight(.semibold))
            Spacer()
        }
    }

    private func sectionHeader<Content: View>(
        _ title: String,
        systemImage: String,
        @ViewBuilder trailing: () -> Content
    ) -> some View {
        HStack {
            Label(title, systemImage: systemImage)
                .font(.title3.weight(.semibold))
            Spacer()
            trailing()
        }
    }

    private var closeBehaviorBinding: Binding<String> {
        Binding(
            get: { settings.value.closeBehavior },
            set: { newValue in settings.update { $0.closeBehavior = newValue } }
        )
    }

    private var restoreLayoutBinding: Binding<Bool> {
        Binding(
            get: { settings.value.restoreLastLayoutOnStartup },
            set: { newValue in settings.update { $0.restoreLastLayoutOnStartup = newValue } }
        )
    }
}

@MainActor
struct ProfileEditorView: View {
    let original: TunnelProfile
    let onSave: (TunnelProfile) -> Void
    @Environment(\.dismiss) private var dismiss

    @State private var name: String
    @State private var sshHost: String
    @State private var sshPort: String
    @State private var user: String
    @State private var remoteHost: String
    @State private var remotePort: String
    @State private var localPort: String

    init(profile: TunnelProfile, onSave: @escaping (TunnelProfile) -> Void) {
        original = profile
        self.onSave = onSave
        _name = State(initialValue: profile.name)
        _sshHost = State(initialValue: profile.sshHost)
        _sshPort = State(initialValue: String(profile.sshPort))
        _user = State(initialValue: profile.user)
        _remoteHost = State(initialValue: profile.remoteHost)
        _remotePort = State(initialValue: String(profile.remotePort))
        _localPort = State(initialValue: String(profile.localPort))
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text(original.sshHost.isEmpty ? "添加 SSH 隧道" : "编辑 SSH 隧道")
                .font(.title2.weight(.semibold))
            Form {
                TextField("名称", text: $name)
                TextField("SSH 主机", text: $sshHost)
                TextField("SSH 端口", text: $sshPort)
                TextField("用户名", text: $user)
                TextField("远端主机", text: $remoteHost)
                TextField("远端端口", text: $remotePort)
                TextField("本地端口", text: $localPort)
            }
            HStack {
                Spacer()
                Button("取消") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("保存") { save() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(!canSave)
            }
        }
        .padding(24)
        .frame(width: 420)
    }

    private var canSave: Bool {
        let candidate = makeProfile()
        return candidate.isValid
    }

    private func makeProfile() -> TunnelProfile {
        TunnelProfile(
            id: original.id,
            name: name.trimmingCharacters(in: .whitespacesAndNewlines),
            sshHost: sshHost.trimmingCharacters(in: .whitespacesAndNewlines),
            sshPort: Int(sshPort) ?? 0,
            user: user.trimmingCharacters(in: .whitespacesAndNewlines),
            remoteHost: remoteHost.trimmingCharacters(in: .whitespacesAndNewlines),
            remotePort: Int(remotePort) ?? 0,
            localPort: Int(localPort) ?? 0
        )
    }

    private func save() {
        guard canSave else { return }
        onSave(makeProfile())
        dismiss()
    }
}

@MainActor
struct PasswordPromptView: View {
    let profile: TunnelProfile
    let onSave: (String) -> Void
    @Environment(\.dismiss) private var dismiss
    @State private var password = ""

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("输入 SSH 密码")
                .font(.title2.weight(.semibold))
            Text(profile.displayName)
                .foregroundStyle(.secondary)
            SecureField("密码", text: $password)
                .textFieldStyle(.roundedBorder)
            HStack {
                Spacer()
                Button("取消") { dismiss() }
                    .keyboardShortcut(.cancelAction)
                Button("保存并连接") {
                    onSave(password)
                    dismiss()
                }
                .keyboardShortcut(.defaultAction)
                .disabled(password.isEmpty)
            }
        }
        .padding(24)
        .frame(width: 360)
    }
}
