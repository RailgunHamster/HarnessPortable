import Foundation
import Combine
import CryptoKit
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
    case failed(String)
}

/// Lightweight in-app updater for the macOS port.
///
/// Windows rides on Velopack (Setup + portable zip both self-update); macOS has
/// no equivalent pipeline here, so this reads the same style of feed the
/// Android app uses (`macos.json` with version / dmg / sha256 / notes), shows
/// download progress like 2.6.10, and then hands the verified dmg to the user —
/// macOS won't let a running app replace itself, so the install step mounts
/// the dmg and the user drags the new app over.
@MainActor
final class UpdateService: NSObject, ObservableObject {
    @Published private(set) var phase: UpdatePhase = .idle
    @Published private(set) var notes: String = ""
    @Published private(set) var feed: MacUpdateFeed?

    private let settings: SettingsStore
    private var downloadTask: URLSessionDownloadTask?
    private var downloadSession: URLSession?
    private var expectedDownloadBytes: Int64 = 0
    private var checkedProxy = UpdateProxy()
    private var checkedBase: URL?

    init(settings: SettingsStore) {
        self.settings = settings
    }

    func resetSourceResult() {
        switch phase { case .checking, .downloading: return; default: break }
        feed = nil; notes = ""; phase = .idle
        checkedBase = nil
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
        let source=settings.value.updateSourceSelected
        return Self.repositoryBase(source == "github" ? settings.value.updateGithubUrl : settings.value.updateCnbUrl, source: source)
    }

    func check() {
        switch phase {
        case .checking, .downloading:
            return
        default:
            break
        }
        phase = .checking

        guard let base = feedBase else {
            phase = .failed("更新源不可用：请检查 CNB / GitHub 仓库地址。")
            return
        }

        let feedURL = base.appendingPathComponent("macos.json")
        feed = nil; checkedBase = base
        let proxy = UpdateProxy(settings: settings.value)
        checkedProxy = proxy
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
                if let http = response as? HTTPURLResponse, !(200..<300).contains(http.statusCode) {
                    throw URLError(.badServerResponse)
                }
                data = payload
            }
            let decoded = try JSONDecoder().decode(MacUpdateFeed.self, from: data)
            feed = decoded
            notes = decoded.notes ?? ""
            if Self.isVersion(decoded.version, newerThan: Self.currentVersion) {
                phase = .available(version: decoded.version)
            } else {
                phase = .upToDate
            }
        } catch {
            feed = nil
            phase = .failed("读取更新源失败：\(error.localizedDescription)")
        }
    }

    func download() {
        guard let feed,
              case .available(let version) = phase,
              let base = checkedBase else { return }

        let absolute = URL(string: feed.dmg)
        let dmgURL = ["http", "https"].contains(absolute?.scheme?.lowercased() ?? "") ? absolute! : base.appendingPathComponent(feed.dmg)
        phase = .downloading(fraction: 0)
        expectedDownloadBytes = 0

        installVersion = version
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

    fileprivate func bridgeDidFinish(at location: URL) {
        defer { try? FileManager.default.removeItem(at: location) }
        downloadTask = nil
        downloadSession?.invalidateAndCancel()
        downloadSession = nil

        do {
            if let expected = feed?.sha256, !expected.isEmpty {
                let data = try Data(contentsOf: location)
                let digest = SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
                if !digest.equalsIgnoreCase(expected) {
                    throw NSError(
                        domain: "HarnessPortable.Update",
                        code: 1,
                        userInfo: [NSLocalizedDescriptionKey: "SHA256 校验失败（下载可能不完整）"]
                    )
                }
            }
            let destination = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
                ?? FileManager.default.temporaryDirectory
            let name = URL(string: feed?.dmg ?? "")?.lastPathComponent
            let target = destination.appendingPathComponent(name?.isEmpty == false ? name! : "HarnessPortable.dmg")
            try? FileManager.default.removeItem(at: target)
            try FileManager.default.moveItem(at: location, to: target)
            phase = .readyToInstall(version: installVersion)
        } catch {
            phase = .failed("下载失败：\(error.localizedDescription)")
        }
    }

    fileprivate func bridgeDidFail(_ error: Error) {
        downloadTask = nil
        downloadSession?.invalidateAndCancel()
        downloadSession = nil
        guard case .downloading = phase else { return }
        phase = .failed("下载失败：\(error.localizedDescription)")
    }

    /// Mounts the downloaded dmg so the user can drag the new app over; the
    /// running app cannot replace itself on macOS.
    func openInstaller() {
        let name = feed?.dmg ?? "HarnessPortable.dmg"
        let destination = FileManager.default.urls(for: .downloadsDirectory, in: .userDomainMask).first
            ?? FileManager.default.temporaryDirectory
        let target = destination.appendingPathComponent(name)
        NSWorkspace.shared.open(target)
    }
}

private extension String {
    func equalsIgnoreCase(_ other: String) -> Bool {
        caseInsensitiveCompare(other) == .orderedSame
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
            if let response = downloadTask.response as? HTTPURLResponse, !(200..<300).contains(response.statusCode) {
                throw URLError(.badServerResponse)
            }
            // URLSession deletes location after this callback returns.
            let staged = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".dmg")
            try FileManager.default.moveItem(at: location, to: staged)
            Task { @MainActor in owner.bridgeDidFinish(at: staged) }
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
