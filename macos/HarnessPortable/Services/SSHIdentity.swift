import Foundation

enum SSHIdentity {
    static let defaultFileNames = ["id_ed25519", "id_ecdsa", "id_rsa"]

    static var sshDirectory: URL {
        FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".ssh", isDirectory: true)
    }

    static func expandPath(_ path: String) -> String {
        let trimmed = path.trimmingCharacters(in: .whitespacesAndNewlines)
        if trimmed.isEmpty { return "" }
        let home = FileManager.default.homeDirectoryForCurrentUser
        if trimmed == "~" { return home.path }
        if trimmed.hasPrefix("~/") {
            return home.appendingPathComponent(String(trimmed.dropFirst(2))).path
        }
        if (trimmed as NSString).isAbsolutePath { return trimmed }
        return sshDirectory.appendingPathComponent(trimmed).path
    }

    static func hasUsableKey(identityFile: String) -> Bool {
        let specified = identityFile.trimmingCharacters(in: .whitespacesAndNewlines)
        if !specified.isEmpty {
            return FileManager.default.isReadableFile(atPath: expandPath(specified))
        }
        if defaultFileNames.contains(where: {
            FileManager.default.isReadableFile(atPath: sshDirectory.appendingPathComponent($0).path)
        }) {
            return true
        }
        if let sock = ProcessInfo.processInfo.environment["SSH_AUTH_SOCK"], !sock.isEmpty {
            return true
        }
        return false
    }

    static func looksLikeAuthenticationFailure(_ message: String) -> Bool {
        let lower = message.lowercased()
        return lower.contains("permission denied") ||
            lower.contains("authentication") ||
            lower.contains("password") ||
            lower.contains("keyboard-interactive") ||
            lower.contains("publickey") ||
            lower.contains("identity file") ||
            lower.contains("no such identity") ||
            lower.contains("too many") ||
            lower.contains("private key") ||
            message.contains("认证") ||
            message.contains("私钥") ||
            message.contains("未保存密码") ||
            message.contains("密码")
    }

    static func sshArguments(identityFile: String, hasPassword: Bool) -> [String] {
        var args = ["-o", "NumberOfPasswordPrompts=1"]
        let expanded = expandPath(identityFile)
        if !expanded.isEmpty {
            args += ["-i", expanded, "-o", "IdentitiesOnly=yes"]
        }
        if hasPassword {
            args += ["-o", "PreferredAuthentications=publickey,keyboard-interactive,password"]
        } else {
            args += [
                "-o", "PreferredAuthentications=publickey",
                "-o", "BatchMode=yes",
                "-o", "PasswordAuthentication=no",
                "-o", "KbdInteractiveAuthentication=no"
            ]
        }
        return args
    }

    static func sshEnvironment(askpass: URL?, passwordFile: URL?) -> [String: String] {
        var environment = ProcessInfo.processInfo.environment
        if let askpass, let passwordFile {
            environment["SSH_ASKPASS"] = askpass.path
            environment["SSH_ASKPASS_REQUIRE"] = "force"
            environment["DISPLAY"] = "1"
            environment["HARNESS_PASSWORD_FILE"] = passwordFile.path
        } else {
            environment["SSH_ASKPASS_REQUIRE"] = "never"
        }
        return environment
    }
}
