import Foundation
import Combine
import AppKit

/// One entry of the `macos.json` update feed, written next to the dmg by
/// `scripts/release-macos.ps1` (same shape family as the Android feed).
struct MacUpdateFeed: Codable {
    var version: String
    var dmg: String
    var sha256: String?
    var notes: String?
}

enum UpdatePhase: Equatable {
    case idle
    case checking
    case upToDate
    case available(version: String)
    case downloading(fraction: Double)
    case readyToInstall(version: String)
    case installing
    case failed(String)
}

/// Reads the macOS DMG feed and hands verified official releases to the installer.
@MainActor
final class UpdateService: NSObject, ObservableObject {
    @Published private(set) var phase: UpdatePhase = .idle
    @Published private(set) var notes: String = ""
    @Published private(set) var feed: MacUpdateFeed?
    @Published private(set) var installIssue: String?

    private let settings: SettingsStore
    private let feedBaseOverride: URL?
    private let downloadFolder: URL
    private let openImage: (URL) -> Bool
    private var downloadTask: URLSessionDownloadTask?
    private var downloadSession: URLSession?
    private var expectedDownloadBytes: Int64 = 0
    private var checkedProxy = UpdateProxy()
    private var checkedBase: URL?
    private var checkedFeedWasHTTPS = false
    private var downloadedPackage: URL?
    private var downloadableFromOfficialSource = false

    var isBusy: Bool {
        switch phase { case .checking, .downloading, .installing: return true; default: return false }
    }

    var canInstallAutomatically: Bool {
        downloadableFromOfficialSource && downloadedPackage != nil && MacUpdateTransition.canReplace()
    }

    init(settings: SettingsStore, feedBaseOverride: URL? = nil, downloadFolder: URL? = nil,
        openImage: ((URL) -> Bool)? = nil) {
        self.settings = settings
        self.feedBaseOverride = feedBaseOverride
        self.downloadFolder = downloadFolder ?? FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.temporaryDirectory
        self.openImage = openImage ?? { NSWorkspace.shared.open($0) }
    }

    func resetSourceResult() {
        guard !isBusy else { return }
        feed = nil; notes = ""; phase = .idle; installIssue = nil
        downloadedPackage = nil; downloadableFromOfficialSource = false
        checkedBase = nil; checkedFeedWasHTTPS = false
    }

    nonisolated static let currentVersion: String = {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"
    }()

    /// Numeric comparison of dotted versions; nil segments count as 0, and a
    /// longer tail only wins when it is actually non-zero (2.6.13 < 2.6.13.1).
    nonisolated static func isVersion(_ candidate: String, newerThan current: String) -> Bool {
        let lhs = candidate.split(separator: ".").map { Int($0) ?? 0 }
        let rhs = current.split(separator: ".").map { Int($0) ?? 0 }
        let count = max(lhs.count, rhs.count)
        for index in 0..<count {
            let l = index < lhs.count ? lhs[index] : 0
            let r = index < rhs.count ? rhs[index] : 0
            if l != r { return l > r }
        }
        return false
    }

    /// Only network feeds remain supported; retired mounted-share paths are rejected.
    nonisolated static func resolveReadableURL(_ raw: String) -> URL? {
        guard let url = URL(string: raw.trimmingCharacters(in: .whitespacesAndNewlines)),
              ["http", "https"].contains(url.scheme?.lowercased() ?? ""), url.host != nil,
              url.user == nil, url.password == nil else { return nil }
        return url
    }

    nonisolated static func repositoryBase(_ raw: String, source: String) -> URL? {
        guard let url = resolveReadableURL(raw), url.scheme == "https", url.query == nil, url.fragment == nil,
              url.port == nil || url.port == 443 else { return nil }
        let domain = source == "github" ? "github.com" : "cnb.cool"
        guard url.host?.lowercased() == domain else { return nil }
        var path = url.path.components(separatedBy: "/-/")[0].trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        if path.hasSuffix(".git") { path = String(path.dropLast(4)) }
        let parts = path.split(separator: "/")
        guard parts.count >= 2 else { return nil }
        if source == "github" { return URL(string: "https://github.com/\(parts[0])/\(parts[1])/releases/latest/download") }
        return URL(string: "https://cnb.cool/\(path)/-/git/raw/updates")
    }

    private var feedBase: URL? {
        if let feedBaseOverride { return feedBaseOverride }
        let source=settings.value.updateSourceSelected
        return Self.repositoryBase(source == "github" ? settings.value.updateGithubUrl : settings.value.updateCnbUrl, source: source)
    }

    func check() {
        guard !isBusy else { return }
        downloadedPackage = nil; downloadableFromOfficialSource = false; installIssue = nil
        phase = .checking

        guard let base = feedBase else {
            MacUpdateLog.record("check.invalid_source")
            phase = .failed("更新源不可用：请检查 CNB / GitHub 仓库地址。")
            return
        }

        let feedURL = base.appendingPathComponent("macos.json")
        feed = nil; checkedBase = base; checkedFeedWasHTTPS = false
        let proxy = UpdateProxy(settings: settings.value)
        checkedProxy = proxy
        MacUpdateLog.record("check.started", "current=\(MacUpdateLog.version(Self.currentVersion)) source=\(base.host ?? "unknown") proxy=\(proxy.enabled)")
        Task { await loadFeed(from: feedURL, proxy: proxy) }
    }

    private func loadFeed(from url: URL, proxy: UpdateProxy) async {
        do {
            let data: Data
            if url.isFileURL {
                data = try Data(contentsOf: url)
            } else {
                let session = URLSession(configuration: try proxy.configuration())
                defer { session.finishTasksAndInvalidate() }
                let (payload, response) = try await session.data(from: url)
                if let http = response as? HTTPURLResponse {
                    MacUpdateLog.record("check.http", "status=\(http.statusCode)")
                    if !(200..<300).contains(http.statusCode) { throw URLError(.badServerResponse) }
                }
                checkedFeedWasHTTPS = response.url?.scheme == "https"
                data = payload
            }
            let decoded = try JSONDecoder().decode(MacUpdateFeed.self, from: data)
            feed = decoded
            notes = decoded.notes ?? ""
            if Self.isVersion(decoded.version, newerThan: Self.currentVersion) {
                phase = .available(version: decoded.version)
                MacUpdateLog.record("check.available", "version=\(MacUpdateLog.version(decoded.version))")
            } else {
                phase = .upToDate
                MacUpdateLog.record("check.up_to_date", "published=\(MacUpdateLog.version(decoded.version))")
            }
        } catch {
            MacUpdateLog.failure("check.failed", error)
            feed = nil
            phase = .failed("读取更新源失败：\(error.localizedDescription)")
        }
    }

    func download() {
        guard let feed,
              case .available(let version) = phase,
              let base = checkedBase else { return }

        let dmgURL = Self.resolveReadableURL(feed.dmg) ?? base.appendingPathComponent(feed.dmg)
        downloadableFromOfficialSource = checkedFeedWasHTTPS && MacUpdateTransition.officialPackage(base: base, package: dmgURL,
            version: feed.version, sha256: feed.sha256)
        phase = .downloading(fraction: 0)
        expectedDownloadBytes = 0

        installVersion = version
        MacUpdateLog.record("download.started", "version=\(MacUpdateLog.version(version)) official=\(downloadableFromOfficialSource)")
        if dmgURL.isFileURL {
            Task {
                do {
                    let staged = try await Task.detached {
                        let target = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".dmg")
                        try FileManager.default.copyItem(at: dmgURL, to: target)
                        return target
                    }.value
                    bridgeDidFinish(at: staged)
                } catch { bridgeDidFail(error) }
            }
            return
        }
        let configuration: URLSessionConfiguration
        do { configuration = try checkedProxy.configuration() }
        catch { bridgeDidFail(error); return }
        let session = URLSession(configuration: configuration, delegate: DownloadProgressBridge(self), delegateQueue: nil)
        downloadSession = session
        let task = session.downloadTask(with: dmgURL)
        downloadTask = task
        task.resume()
    }

    private var installVersion = ""

    fileprivate func bridgeDidWrite(bytesWritten: Int64, totalBytesWritten: Int64, totalBytesExpected: Int64) {
        guard case .downloading = phase else { return }
        if totalBytesExpected > 0 {
            expectedDownloadBytes = totalBytesExpected
            phase = .downloading(fraction: Double(totalBytesWritten) / Double(totalBytesExpected))
        } else if expectedDownloadBytes > 0 {
            phase = .downloading(fraction: Double(totalBytesWritten) / Double(expectedDownloadBytes))
        } else {
            phase = .downloading(fraction: 0)
        }
    }

    fileprivate func bridgeDidFinish(at location: URL, downloadedOverHTTPS: Bool = false) {
        downloadTask = nil
        downloadSession?.invalidateAndCancel()
        downloadSession = nil
        downloadableFromOfficialSource = downloadableFromOfficialSource && downloadedOverHTTPS

        let expected = feed?.sha256
        let version = installVersion
        let destination = downloadFolder
        let name = URL(string: feed?.dmg ?? "")?.lastPathComponent ?? ""
        let invalidFilename = CharacterSet.controlCharacters.union(CharacterSet(charactersIn: "/\\:"))
        let safeName = name.lowercased().hasSuffix(".dmg") && name.rangeOfCharacter(from: invalidFilename) == nil
            ? name : "HarnessPortable.dmg"
        Task { @MainActor in
            defer { try? FileManager.default.removeItem(at: location) }
            do {
                let target = try await Task.detached {
                    if let expected, !expected.isEmpty,
                       try MacUpdateTransition.digest(location).caseInsensitiveCompare(expected) != .orderedSame {
                        MacUpdateLog.record("download.checksum_mismatch")
                        throw MacUpdateIssue.invalidPackage
                    }
                    let fm = FileManager.default
                    let target = destination.appendingPathComponent(safeName)
                    do {
                        try fm.moveItem(at: location, to: target)
                        return target
                    } catch CocoaError.fileWriteFileExists {
                        let alternate = destination.appendingPathComponent(UUID().uuidString + "-" + safeName)
                        try fm.moveItem(at: location, to: alternate)
                        return alternate
                    }
                }.value
                downloadedPackage = target
                phase = .readyToInstall(version: version)
                MacUpdateLog.record("download.ready", "checksumVerified=\(expected?.isEmpty == false) automatic=\(canInstallAutomatically)")
            } catch {
                MacUpdateLog.failure("download.validation_or_save_failed", error)
                phase = .failed("下载失败：\(error.localizedDescription)")
            }
        }
    }

    fileprivate func bridgeDidFail(_ error: Error) {
        downloadTask = nil
        downloadSession?.invalidateAndCancel()
        downloadSession = nil
        guard case .downloading = phase else { return }
        MacUpdateLog.failure("download.failed", error)
        phase = .failed("下载失败：\(error.localizedDescription)")
    }

    func openInstaller() {
        guard case .readyToInstall = phase, let target = downloadedPackage else { return }
        let opened = openImage(target)
        MacUpdateLog.record("installer.open_image", "opened=\(opened)")
        if !opened { installIssue = "无法打开安装镜像，请重新下载。" }
    }

    func installAndRelaunch() {
        guard case .readyToInstall(let version) = phase, canInstallAutomatically,
              let package = downloadedPackage, let sha256 = feed?.sha256 else { return }
        let request = MacUpdateRequest(parent: ProcessInfo.processInfo.processIdentifier, app: Bundle.main.bundleURL,
            from: Self.currentVersion, to: version, sha256: sha256, appearance: settings.value.appearance)
        installIssue = nil
        phase = .installing
        MacUpdateLog.record("install.requested", "from=\(MacUpdateLog.version(request.from)) to=\(MacUpdateLog.version(request.to))")
        Task { @MainActor in
            do {
                try await MacUpdateTransition.prepareAndLaunch(package: package, request: request)
                MacUpdateLog.record("install.quitting_for_replacement")
                NSApplication.shared.terminate(nil)
            } catch {
                MacUpdateLog.failure("install.handoff_failed", error)
                installIssue = error.localizedDescription
                phase = .readyToInstall(version: version)
            }
        }
    }
}

/// URLSession's delegate callbacks arrive on its own queue; hop to main.
private final class DownloadProgressBridge: NSObject, URLSessionDownloadDelegate {
    private let owner: UpdateService

    init(_ owner: UpdateService) {
        self.owner = owner
    }

    func urlSession(
        _ session: URLSession,
        downloadTask: URLSessionDownloadTask,
        didWriteData bytesWritten: Int64,
        totalBytesWritten: Int64,
        totalBytesExpectedToWrite: Int64
    ) {
        let owner = self.owner
        Task { @MainActor in
            owner.bridgeDidWrite(
                bytesWritten: bytesWritten,
                totalBytesWritten: totalBytesWritten,
                totalBytesExpected: totalBytesExpectedToWrite
            )
        }
    }

    func urlSession(_ session: URLSession, downloadTask: URLSessionDownloadTask, didFinishDownloadingTo location: URL) {
        let owner = self.owner
        do {
            if let response = downloadTask.response as? HTTPURLResponse {
                MacUpdateLog.record("download.http", "status=\(response.statusCode) bytes=\(downloadTask.countOfBytesReceived)")
                if !(200..<300).contains(response.statusCode) { throw URLError(.badServerResponse) }
            }
            // URLSession deletes location after this callback returns.
            let staged = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".dmg")
            try FileManager.default.moveItem(at: location, to: staged)
            let downloadedOverHTTPS = downloadTask.response?.url?.scheme == "https"
            Task { @MainActor in owner.bridgeDidFinish(at: staged, downloadedOverHTTPS: downloadedOverHTTPS) }
        } catch {
            Task { @MainActor in owner.bridgeDidFail(error) }
        }
    }

    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        guard let error else { return }
        let owner = self.owner
        Task { @MainActor in
            owner.bridgeDidFail(error)
        }
    }
}
