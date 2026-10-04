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

    init(settings: SettingsStore) {
        self.settings = settings
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

    /// Maps `smb://server/share/path…` onto its mounted volume under
    /// `/Volumes`. macOS mounts `\\server\share` either as `/Volumes/share` or,
    /// when several servers expose the same share name, as
    /// `/Volumes/share@server` (or `-server`); the first existing match wins.
    /// Plain paths and http(s) URLs pass through unchanged.
    nonisolated static func resolveReadableURL(_ raw: String) -> URL? {
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return nil }

        if let url = URL(string: trimmed), let scheme = url.scheme?.lowercased() {
            if scheme == "http" || scheme == "https" || scheme == "file" { return url }
            if scheme == "smb" || scheme == "cifs" {
                // smb://server/share/rest -> /Volumes[/share@server | /share]/rest
                let server = url.host ?? ""
                var parts = url.pathComponents.filter { $0 != "/" }
                guard let share = parts.first else { return nil }
                let rest = parts.dropFirst()
                let candidates = [
                    "/Volumes/\(share)@\(server)",
                    "/Volumes/\(share)-\(server)",
                    "/Volumes/\(share)",
                ]
                let fm = FileManager.default
                for base in candidates where fm.fileExists(atPath: base) {
                    let path = rest.isEmpty ? base : base + "/" + rest.joined(separator: "/")
                    return URL(fileURLWithPath: path)
                }
                return nil
            }
            return nil
        }

        // Bare path: assume the user typed a mount point or local folder.
        return URL(fileURLWithPath: trimmed)
    }

    private var feedBase: URL? {
        let value = settings.value.updateSourceSelected == "github"
            ? settings.value.updateGithubUrl
            : settings.value.updateHomeUrl
        guard let base = Self.resolveReadableURL(value) else { return nil }
        if base.scheme?.lowercased() == "http" || base.scheme?.lowercased() == "https" {
            return base
        }
        guard FileManager.default.fileExists(atPath: base.path) else { return nil }
        return base
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
            phase = .failed("更新源不可用：请检查地址，或先在访达中连接共享（⌘K → smb://server-home/public）。")
            return
        }

        let feedURL = base.appendingPathComponent("macos.json")
        Task { await loadFeed(from: feedURL) }
    }

    private func loadFeed(from url: URL) async {
        do {
            let data: Data
            if url.isFileURL {
                data = try Data(contentsOf: url)
            } else {
                let (payload, response) = try await URLSession.shared.data(from: url)
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
              let base = feedBase else { return }

        let dmgURL = base.appendingPathComponent(feed.dmg)
        phase = .downloading(fraction: 0)
        expectedDownloadBytes = 0

        let session = URLSession(configuration: .default, delegate: DownloadProgressBridge(self), delegateQueue: nil)
        downloadSession = session
        let task = session.downloadTask(with: dmgURL)
        downloadTask = task
        task.resume()

        // The bridge on a background queue drives main-actor updates through
        // this helper; keep the version for the completion state.
        installVersion = version
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
            let target = destination.appendingPathComponent(feed?.dmg ?? "HarnessPortable.dmg")
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
        Task { @MainActor in
            owner.bridgeDidFinish(at: location)
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
