import AppKit
import CryptoKit
import Darwin
import SwiftUI

struct MacUpdateRequest: Codable {
    let parent: Int32
    let app: URL
    let from: String
    let to: String
    let sha256: String
    let appearance: String
}

enum MacUpdateIssue: LocalizedError {
    case invalidPackage, preparation, exitTimeout, replacement, relaunch

    var errorDescription: String? {
        switch self {
        case .invalidPackage: return "安装包校验失败。"
        case .preparation: return "无法准备安装，请打开安装镜像手动更新。"
        case .exitTimeout: return "等待应用退出超时，请退出后打开安装镜像。"
        case .replacement: return "无法替换应用，请打开安装镜像手动更新。"
        case .relaunch: return "新版本未能打开，已恢复旧版本。"
        }
    }
}

enum MacUpdateTransition {
    static let bundleID = "com.harness.portable"
    private static let helperFlag = "--harness-install-update"

    static func officialPackage(base: URL, package: URL, version: String, sha256: String?) -> Bool {
        guard version.range(of: #"^[0-9]+\.[0-9]+\.[0-9]+$"#, options: .regularExpression) != nil,
              sha256?.range(of: #"^[0-9a-fA-F]{64}$"#, options: .regularExpression) != nil,
              base.scheme == "https", base.port == nil || base.port == 443,
              base.user == nil, base.password == nil,
              package.scheme == "https", package.port == nil || package.port == 443,
              package.user == nil, package.password == nil, package.query == nil, package.fragment == nil,
              package.host?.lowercased() == base.host?.lowercased() else { return false }
        let repo = "/railgunhamster/harnessportable"
        let name = "/HarnessPortable-\(version).dmg"
        switch base.host?.lowercased() {
        case "cnb.cool":
            return base.path.lowercased() == repo + "/-/git/raw/updates"
                && package.path.lowercased() == (repo + "/-/releases/download/v\(version)" + name).lowercased()
        case "github.com":
            return base.path.lowercased() == repo + "/releases/latest/download"
                && ["/releases/latest/download", "/releases/download/v\(version)"].contains {
                    package.path.lowercased() == (repo + $0 + name).lowercased()
                }
        default: return false
        }
    }

    static func canReplace(_ bundle: Bundle = .main) -> Bool {
        let app = bundle.bundleURL.standardizedFileURL
        let parent = app.deletingLastPathComponent()
        let homeApplications = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Applications").path
        return bundle.bundleIdentifier == bundleID && app.pathExtension == "app"
            && ["/Applications", homeApplications].contains(parent.path)
            && app.resolvingSymlinksInPath().path == app.path
            && FileManager.default.isWritableFile(atPath: parent.path)
            && FileManager.default.isWritableFile(atPath: app.path)
    }

    static func digest(_ file: URL) throws -> String {
        let reader = try FileHandle(forReadingFrom: file)
        defer { try? reader.close() }
        var sha = SHA256()
        while let data = try reader.read(upToCount: 1024 * 1024), !data.isEmpty { sha.update(data: data) }
        return sha.finalize().map { String(format: "%02x", $0) }.joined()
    }

    static func helperRequest(_ arguments: [String]) -> URL? {
        guard arguments.count == 3, arguments[1] == helperFlag else { return nil }
        return URL(fileURLWithPath: arguments[2], isDirectory: true)
    }

    @MainActor
    static func prepareAndLaunch(package: URL, request: MacUpdateRequest) async throws {
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent("hp-update-" + UUID().uuidString)
        guard let executable = Bundle.main.executableURL else { throw MacUpdateIssue.preparation }
        let helper = directory.appendingPathComponent("HarnessPortableUpdater")
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: false,
            attributes: [.posixPermissions: 0o700])
        var process: Process?
        do {
            try await Task.detached {
                try FileManager.default.copyItem(at: executable, to: helper)
                try FileManager.default.copyItem(at: package, to: directory.appendingPathComponent("update.dmg"))
                try JSONEncoder().encode(request).write(to: directory.appendingPathComponent("request.json"), options: .atomic)
            }.value
            let child = Process()
            child.executableURL = helper
            child.arguments = [helperFlag, directory.path]
            child.standardInput = FileHandle.nullDevice
            child.standardOutput = FileHandle.nullDevice
            child.standardError = FileHandle.nullDevice
            try child.run()
            process = child
            let deadline = ProcessInfo.processInfo.systemUptime + 90
            while child.isRunning && ProcessInfo.processInfo.systemUptime < deadline {
                if FileManager.default.fileExists(atPath: directory.appendingPathComponent("ready").path) {
                    try Data().write(to: directory.appendingPathComponent("commit"), options: .atomic)
                    return
                }
                try await Task.sleep(nanoseconds: 100_000_000)
            }
            throw MacUpdateIssue.preparation
        } catch {
            if let process, process.isRunning {
                // Ask the helper to leave after its current disk operation. It
                // owns cleanup from the moment it starts.
                try? Data().write(to: directory.appendingPathComponent("cancel"), options: .atomic)
            } else {
                try? FileManager.default.removeItem(at: directory)
            }
            throw error
        }
    }

    @MainActor
    static func runHelper(_ directory: URL) {
        let name = directory.lastPathComponent
        let temporary = FileManager.default.temporaryDirectory.resolvingSymlinksInPath().path
        guard name.hasPrefix("hp-update-"), UUID(uuidString: String(name.dropFirst(10))) != nil,
              directory.deletingLastPathComponent().resolvingSymlinksInPath().path == temporary,
              directory.resolvingSymlinksInPath().path == URL(fileURLWithPath: temporary).appendingPathComponent(name).path,
              let data = try? Data(contentsOf: directory.appendingPathComponent("request.json")),
              let request = try? JSONDecoder().decode(MacUpdateRequest.self, from: data),
              request.parent > 1, request.app.isFileURL, UpdateService.isVersion(request.to, newerThan: request.from),
              Bundle(url: request.app)?.bundleIdentifier == bundleID else { exit(2) }
        let application = NSApplication.shared
        application.setActivationPolicy(.accessory)
        let style: ColorScheme? = request.appearance == "dark" ? .dark : request.appearance == "light" ? .light : nil
        let icon = NSWorkspace.shared.icon(forFile: request.app.path)
        let panel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 470, height: 184),
            styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        panel.title = "Harness Portable 更新"
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.hidesOnDeactivate = false
        panel.isMovable = false
        panel.isFloatingPanel = false
        panel.ignoresMouseEvents = true
        panel.isReleasedWhenClosed = false
        let presentation = MacUpdatePresentation()
        panel.contentView = NSHostingView(rootView: MacUpdateSplashView(presentation: presentation,
            from: request.from, to: request.to, icon: icon).preferredColorScheme(style))
        panel.center()
        panel.orderFrontRegardless()

        Task { @MainActor in
            let fm = FileManager.default
            var staged: URL?
            var swapped = false
            var keepPrevious = false
            var parentExited = false
            defer {
                if let staged, !keepPrevious { try? fm.removeItem(at: staged.deletingLastPathComponent()) }
                try? fm.removeItem(at: directory)
                panel.close()
                exit(swapped && !keepPrevious ? 0 : 1)
            }
            do {
                MacUpdateLog.record("helper.preparing")
                staged = try await Task.detached { try stage(request, in: directory) }.value
                guard let staged else { throw MacUpdateIssue.preparation }
                let cancel = directory.appendingPathComponent("cancel")
                if fm.fileExists(atPath: cancel.path) { throw CancellationError() }
                try Data().write(to: directory.appendingPathComponent("ready"), options: .atomic)
                MacUpdateLog.record("helper.waiting_for_exit")
                let deadline = ProcessInfo.processInfo.systemUptime + 30
                while kill(request.parent, 0) == 0 || errno == EPERM {
                    if fm.fileExists(atPath: cancel.path) { throw CancellationError() }
                    guard ProcessInfo.processInfo.systemUptime < deadline else { throw MacUpdateIssue.exitTimeout }
                    try await Task.sleep(nanoseconds: 100_000_000)
                }
                guard fm.fileExists(atPath: directory.appendingPathComponent("commit").path),
                      !fm.fileExists(atPath: cancel.path) else { throw CancellationError() }
                parentExited = true
                presentation.stage = .installing
                let plist = request.app.appendingPathComponent("Contents/Info.plist")
                guard (NSDictionary(contentsOf: plist)?["CFBundleShortVersionString"] as? String) == request.from,
                      !NSRunningApplication.runningApplications(withBundleIdentifier: bundleID).contains(where: {
                          $0.bundleURL?.standardizedFileURL.path == request.app.standardizedFileURL.path && !$0.isTerminated
                      }) else { throw MacUpdateIssue.replacement }
                try exchange(staged, request.app)
                swapped = true
                MacUpdateLog.record("helper.bundle_replaced")
                presentation.stage = .reopening
                var launched: NSRunningApplication?
                do {
                    let newApplication = try await reopen(request.app)
                    launched = newApplication
                    let deadline = ProcessInfo.processInfo.systemUptime + 12
                    while !newApplication.isFinishedLaunching && !newApplication.isTerminated {
                        guard ProcessInfo.processInfo.systemUptime < deadline else {
                            MacUpdateLog.record("helper.launch_timed_out")
                            throw MacUpdateIssue.relaunch
                        }
                        try await Task.sleep(nanoseconds: 100_000_000)
                    }
                    try await Task.sleep(nanoseconds: 900_000_000)
                    guard !newApplication.isTerminated else { throw MacUpdateIssue.relaunch }
                    MacUpdateLog.record("helper.relaunched", "version=\(MacUpdateLog.version(request.to))")
                } catch {
                    MacUpdateLog.failure("helper.relaunch_failed", error)
                    // Roll back only after the replacement has stopped. Retain the previous bundle if recovery fails.
                    keepPrevious = true
                    if let launched, !launched.isTerminated {
                        guard launched.bundleURL?.standardizedFileURL.path == request.app.standardizedFileURL.path else {
                            throw MacUpdateIssue.replacement
                        }
                        launched.forceTerminate()
                        let deadline = ProcessInfo.processInfo.systemUptime + 2
                        while !launched.isTerminated && ProcessInfo.processInfo.systemUptime < deadline {
                            try await Task.sleep(nanoseconds: 100_000_000)
                        }
                        guard launched.isTerminated else { throw MacUpdateIssue.replacement }
                    }
                    try exchange(staged, request.app)
                    swapped = false
                    keepPrevious = false
                    MacUpdateLog.record("helper.rolled_back")
                    throw MacUpdateIssue.relaunch
                }
            } catch {
                MacUpdateLog.failure("helper.stopped", error)
                presentation.stage = .failed
                if keepPrevious && swapped, let staged {
                    presentation.failure = "请从 \(staged.path) 恢复旧版本。"
                } else {
                    presentation.failure = error.localizedDescription
                }
                try? await Task.sleep(nanoseconds: 2_500_000_000)
                if parentExited && !swapped { _ = try? await reopen(request.app) }
            }
        }
        application.run()
    }

    static func stage(_ request: MacUpdateRequest, in directory: URL) throws -> URL {
        let fm = FileManager.default
        let image = directory.appendingPathComponent("update.dmg")
        guard request.sha256.range(of: #"^[0-9a-fA-F]{64}$"#, options: .regularExpression) != nil,
              try digest(image).caseInsensitiveCompare(request.sha256) == .orderedSame else { throw MacUpdateIssue.invalidPackage }
        let mount = directory.appendingPathComponent("mount")
        try fm.createDirectory(at: mount, withIntermediateDirectories: false)
        try command("/usr/bin/hdiutil", ["attach", image.path, "-mountpoint", mount.path, "-readonly", "-nobrowse", "-noautoopen"])
        defer { try? command("/usr/bin/hdiutil", ["detach", mount.path]) }
        let source = mount.appendingPathComponent("HarnessPortable.app")
        try validate(source, version: request.to)
        let stage = request.app.deletingLastPathComponent().appendingPathComponent(".harness-update-" + UUID().uuidString)
        try fm.createDirectory(at: stage, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        let target = stage.appendingPathComponent(request.app.lastPathComponent)
        do {
            try command("/usr/bin/ditto", [source.path, target.path])
            try validate(target, version: request.to)
            return target
        } catch {
            try? fm.removeItem(at: stage)
            throw error
        }
    }

    static func exchange(_ first: URL, _ second: URL) throws {
        // A rename exchange keeps the installed path valid even if the updater
        // exits between replacement and relaunch. Both bundles stay on one volume.
        let result = first.withUnsafeFileSystemRepresentation { lhs in
            second.withUnsafeFileSystemRepresentation { rhs in
                renameatx_np(AT_FDCWD, lhs, AT_FDCWD, rhs, UInt32(RENAME_SWAP))
            }
        }
        guard result == 0 else {
            MacUpdateLog.record("helper.exchange_failed", "errno=\(errno)")
            throw MacUpdateIssue.replacement
        }
    }

    private static func validate(_ app: URL, version: String) throws {
        guard app.resolvingSymlinksInPath().lastPathComponent == app.lastPathComponent,
              let bundle = Bundle(url: app), bundle.bundleIdentifier == bundleID,
              bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String == version,
              let executable = bundle.executableURL else { throw MacUpdateIssue.invalidPackage }
        #if arch(arm64)
        let architecture = "arm64"
        #else
        let architecture = "x86_64"
        #endif
        try command("/usr/bin/lipo", ["-verify_arch", architecture, executable.path])
        try command("/usr/bin/codesign", ["--verify", "--deep", "--strict", app.path])
    }

    private static func command(_ executable: String, _ arguments: [String]) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        process.standardInput = FileHandle.nullDevice
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        let finished = DispatchSemaphore(value: 0)
        process.terminationHandler = { _ in finished.signal() }
        try process.run()
        guard finished.wait(timeout: .now() + 25) == .success else {
            process.terminate()
            MacUpdateLog.record("helper.command_timed_out", "tool=\(URL(fileURLWithPath: executable).lastPathComponent)")
            throw MacUpdateIssue.preparation
        }
        guard process.terminationStatus == 0 else {
            MacUpdateLog.record("helper.command_failed", "tool=\(URL(fileURLWithPath: executable).lastPathComponent) exit=\(process.terminationStatus)")
            throw MacUpdateIssue.preparation
        }
    }

    @MainActor
    private static func reopen(_ app: URL) async throws -> NSRunningApplication {
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = true
        configuration.createsNewApplicationInstance = true
        return try await NSWorkspace.shared.openApplication(at: app, configuration: configuration)
    }
}
