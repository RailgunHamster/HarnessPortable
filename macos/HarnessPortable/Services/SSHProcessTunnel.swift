import Foundation

private enum TunnelProcessError: LocalizedError {
    case missingPassword
    case hostResolutionFailed(String)
    case hostKeyScanFailed(String)
    case hostKeyChanged
    case launchFailed(String)
    case noLocalPort(String)

    var errorDescription: String? {
        switch self {
        case .missingPassword:
            return "未保存密码"
        case .hostResolutionFailed(let host):
            return "无法解析主机名：\(host)"
        case .hostKeyScanFailed(let message):
            return "无法读取服务器主机密钥：\(message)"
        case .hostKeyChanged:
            return "服务器主机密钥已改变，拒绝连接（可能存在中间人攻击）"
        case .launchFailed(let message):
            return message
        case .noLocalPort(let message):
            return message
        }
    }
}

final class SSHProcessTunnel {
    private struct ScannedKey {
        var type: String
        var base64: String
    }

    private let keychain: KeychainStore
    private let knownHosts: KnownHostsStore
    private let worker = DispatchQueue(label: "com.harness.portable.ssh", qos: .utility)
    private let stateLock = NSLock()

    private var generation = 0
    private var process: Process?

    var onState: ((TunnelInfo) -> Void)?

    init(keychain: KeychainStore, knownHosts: KnownHostsStore) {
        self.keychain = keychain
        self.knownHosts = knownHosts
    }

    func start(profile: TunnelProfile) {
        let oldProcess: Process?
        let currentGeneration: Int
        stateLock.lock()
        generation += 1
        currentGeneration = generation
        oldProcess = process
        process = nil
        stateLock.unlock()

        oldProcess?.terminate()
        worker.async { [weak self] in
            self?.run(profile: profile, generation: currentGeneration)
        }
    }

    func stop(profile: TunnelProfile) {
        let oldProcess: Process?
        stateLock.lock()
        generation += 1
        oldProcess = process
        process = nil
        stateLock.unlock()

        oldProcess?.terminate()
        removePasswordFile(for: profile.id)
        emit(TunnelInfo(profileID: profile.id, profileName: profile.displayName, status: .stopped, message: "隧道已停止"))
    }

    private func run(profile: TunnelProfile, generation: Int) {
        var backoff: TimeInterval = 3
        var passwordFile: URL?
        defer { removePasswordFile(for: profile.id) }

        while isCurrent(generation) {
            var child: Process?
            do {
                guard let password = keychain.password(for: profile.id) else {
                    emit(TunnelInfo(profileID: profile.id, profileName: profile.displayName, status: .failed, message: TunnelProcessError.missingPassword.localizedDescription))
                    return
                }
                if passwordFile == nil {
                    passwordFile = try preparePasswordFile(for: profile.id, password: password)
                }
                guard let passwordFile else { throw TunnelProcessError.missingPassword }

                emit(TunnelInfo(profileID: profile.id, profileName: profile.displayName, status: .connecting, message: "正在连接…"))
                let resolution = HostResolver.resolve(profile.sshHost)
                guard let resolution else {
                    emit(TunnelInfo(profileID: profile.id, profileName: profile.displayName, status: .failed, message: TunnelProcessError.hostResolutionFailed(profile.sshHost).localizedDescription))
                    return
                }

                let key = try scanHostKey(host: resolution.ip, port: profile.sshPort)
                switch knownHosts.check(host: profile.sshHost, port: profile.sshPort, keyType: key.type, keyBase64: key.base64) {
                case .trustedNew, .trustedMatch:
                    break
                case .changed:
                    throw TunnelProcessError.hostKeyChanged
                }

                let knownHostsFile = try makeKnownHostsFile(profile: profile, resolvedHost: resolution.ip, key: key)
                var lastError: Error?
                var connectedPort = 0

                for offset in 0..<10 {
                    let candidate = profile.localPort + offset
                    guard candidate <= 65535 else { break }
                    do {
                        let launched = try launch(
                            profile: profile,
                            resolvedHost: resolution.ip,
                            localPort: candidate,
                            knownHostsFile: knownHostsFile,
                            passwordFile: passwordFile
                        )
                        child = launched
                        connectedPort = candidate
                        break
                    } catch {
                        lastError = error
                    }
                }

                guard let child, connectedPort > 0 else {
                    throw TunnelProcessError.noLocalPort(lastError?.localizedDescription ?? "无法绑定本地端口")
                }

                stateLock.lock()
                if generation == self.generation {
                    process = child
                }
                stateLock.unlock()

                guard isCurrent(generation) else {
                    child.terminate()
                    return
                }

                backoff = 3
                let via = resolution.source == "tailscale" ? " · Tailscale \(resolution.ip)" : ""
                emit(TunnelInfo(
                    profileID: profile.id,
                    profileName: profile.displayName,
                    status: .connected,
                    message: "127.0.0.1:\(connectedPort) -> \(profile.remoteHost):\(profile.remotePort)\(via)",
                    localPort: connectedPort
                ))

                while child.isRunning && isCurrent(generation) {
                    Thread.sleep(forTimeInterval: 2)
                }

                if !isCurrent(generation) {
                    return
                }

                let errorMessage = processErrorMessage(from: child)
                if isAuthenticationFailure(errorMessage) {
                    emit(TunnelInfo(profileID: profile.id, profileName: profile.displayName, status: .failed, message: "认证失败：\(errorMessage)"))
                    return
                }
                if errorMessage.localizedCaseInsensitiveContains("host key") {
                    emit(TunnelInfo(profileID: profile.id, profileName: profile.displayName, status: .failed, message: TunnelProcessError.hostKeyChanged.localizedDescription))
                    return
                }

                emit(TunnelInfo(profileID: profile.id, profileName: profile.displayName, status: .retrying, message: "连接中断，正在重连…"))
            } catch {
                if !isCurrent(generation) { return }
                let message = error.localizedDescription
                if error is TunnelProcessError {
                    if message.contains("主机密钥") || message == "未保存密码" || message.contains("认证") {
                        emit(TunnelInfo(profileID: profile.id, profileName: profile.displayName, status: .failed, message: message))
                        return
                    }
                }
                if isAuthenticationFailure(message) {
                    emit(TunnelInfo(profileID: profile.id, profileName: profile.displayName, status: .failed, message: "认证失败：\(message)"))
                    return
                }
                emit(TunnelInfo(profileID: profile.id, profileName: profile.displayName, status: .retrying, message: message))
            }

            if let child {
                child.terminate()
                clearProcess(child)
            }

            let deadline = Date().addingTimeInterval(backoff)
            while isCurrent(generation), Date() < deadline {
                Thread.sleep(forTimeInterval: 0.25)
            }
            backoff = min(backoff * 2, 30)
        }
    }

    private func scanHostKey(host: String, port: Int) throws -> ScannedKey {
        let command = Process()
        command.executableURL = URL(fileURLWithPath: "/usr/bin/ssh-keyscan")
        command.arguments = ["-T", "10", "-p", String(port), host]
        let output = Pipe()
        command.standardOutput = output
        command.standardError = FileHandle.nullDevice
        try command.run()
        command.waitUntilExit()

        guard command.terminationStatus == 0 else {
            throw TunnelProcessError.hostKeyScanFailed("ssh-keyscan 退出码 \(command.terminationStatus)")
        }

        let text = String(data: output.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
        let candidates: [ScannedKey] = text.split(whereSeparator: \.isNewline).compactMap { line in
            let parts = line.split(separator: " ", omittingEmptySubsequences: true)
            guard parts.count >= 3, !parts[0].hasPrefix("#") else { return nil }
            return ScannedKey(type: String(parts[1]), base64: String(parts[2]))
        }
        guard let preferred = candidates.first(where: { $0.type == "ssh-ed25519" }) ?? candidates.first else {
            throw TunnelProcessError.hostKeyScanFailed("没有返回可用的主机密钥")
        }
        return preferred
    }

    private func makeKnownHostsFile(profile: TunnelProfile, resolvedHost: String, key: ScannedKey) throws -> URL {
        let url = AppPaths.knownHostFile(for: profile.id)
        let hostField = profile.sshPort == 22 ? resolvedHost : "[\(resolvedHost)]:\(profile.sshPort)"
        let line = "\(hostField) \(key.type) \(key.base64)\n"
        try Data(line.utf8).write(to: url, options: .atomic)
        return url
    }

    private func launch(
        profile: TunnelProfile,
        resolvedHost: String,
        localPort: Int,
        knownHostsFile: URL,
        passwordFile: URL
    ) throws -> Process {
        let askpass = try prepareAskpass()
        let command = Process()
        command.executableURL = URL(fileURLWithPath: "/usr/bin/ssh")
        let remote = profile.remoteHost.contains(":") ? "[\(profile.remoteHost)]" : profile.remoteHost
        let forward = "127.0.0.1:\(localPort):\(remote):\(profile.remotePort)"
        command.arguments = [
            "-N",
            "-L", forward,
            "-p", String(profile.sshPort),
            "-o", "ExitOnForwardFailure=yes",
            "-o", "StrictHostKeyChecking=yes",
            "-o", "UserKnownHostsFile=\(knownHostsFile.path)",
            "-o", "ServerAliveInterval=15",
            "-o", "ServerAliveCountMax=4",
            "-o", "ConnectTimeout=20",
            "-o", "PreferredAuthentications=keyboard-interactive,password",
            "-o", "PubkeyAuthentication=no",
            "-o", "NumberOfPasswordPrompts=1",
            "\(profile.user)@\(resolvedHost)"
        ]

        var environment = ProcessInfo.processInfo.environment
        environment["SSH_ASKPASS"] = askpass.path
        environment["SSH_ASKPASS_REQUIRE"] = "force"
        environment["DISPLAY"] = "1"
        environment["HARNESS_PROFILE_ID"] = profile.id
        environment["HARNESS_PASSWORD_FILE"] = passwordFile.path
        command.environment = environment
        command.standardInput = FileHandle.nullDevice
        command.standardOutput = FileHandle.nullDevice
        let errors = Pipe()
        command.standardError = errors
        try command.run()

        Thread.sleep(forTimeInterval: 0.8)
        guard command.isRunning else {
            let message = String(data: errors.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8)?
                .trimmingCharacters(in: .whitespacesAndNewlines)
            if isHostKeyFailure(message ?? "") {
                throw TunnelProcessError.hostKeyChanged
            }
            throw TunnelProcessError.launchFailed(message?.isEmpty == false ? message! : "ssh 进程未能建立转发")
        }
        return command
    }

    private func prepareAskpass() throws -> URL {
        let url = AppPaths.askpassScript
        let script = "#!/bin/sh\n[ -n \"$HARNESS_PASSWORD_FILE\" ] || exit 1\n[ -r \"$HARNESS_PASSWORD_FILE\" ] || exit 1\nexec /bin/cat \"$HARNESS_PASSWORD_FILE\"\n"
        if !FileManager.default.fileExists(atPath: url.path) ||
            String(data: (try? Data(contentsOf: url)) ?? Data(), encoding: .utf8) != script {
            try Data(script.utf8).write(to: url, options: .atomic)
        }
        try FileManager.default.setAttributes([.posixPermissions: NSNumber(value: Int(0o700))], ofItemAtPath: url.path)
        return url
    }

    private func preparePasswordFile(for profileID: String, password: String) throws -> URL {
        AppPaths.ensure()
        let url = AppPaths.passwordFile(for: profileID)
        try Data(password.utf8).write(to: url, options: .atomic)
        try FileManager.default.setAttributes([.posixPermissions: NSNumber(value: Int(0o600))], ofItemAtPath: url.path)
        return url
    }

    private func removePasswordFile(for profileID: String) {
        try? FileManager.default.removeItem(at: AppPaths.passwordFile(for: profileID))
    }

    private func processErrorMessage(from process: Process) -> String {
        guard let pipe = process.standardError as? Pipe,
              let text = String(data: pipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) else {
            return "SSH 连接已断开"
        }
        let trimmed = text.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? "SSH 连接已断开" : trimmed
    }

    private func isHostKeyFailure(_ message: String) -> Bool {
        let lower = message.lowercased()
        return lower.contains("host key") ||
            lower.contains("ed25519 key") ||
            lower.contains("hostkey") ||
            lower.contains("主机密钥")
    }

    private func isAuthenticationFailure(_ message: String) -> Bool {
        let lower = message.lowercased()
        return lower.contains("permission denied") ||
            lower.contains("authentication") ||
            lower.contains("password") ||
            lower.contains("keyboard-interactive")
    }

    private func isCurrent(_ expected: Int) -> Bool {
        stateLock.lock()
        defer { stateLock.unlock() }
        return expected == generation
    }

    private func clearProcess(_ candidate: Process) {
        stateLock.lock()
        if process === candidate { process = nil }
        stateLock.unlock()
    }

    private func emit(_ info: TunnelInfo) {
        onState?(info)
    }
}
