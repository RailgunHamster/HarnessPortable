import Darwin
import Foundation

enum MacUpdateLog {
    private static let lock = NSLock()

    static func record(_ event: String, _ fields: String = "") {
        lock.lock()
        defer { lock.unlock() }
        let directory = AppPaths.dataDirectory
        let file = directory.appendingPathComponent("update.log")
        do {
            let fm = FileManager.default
            try fm.createDirectory(at: directory, withIntermediateDirectories: true)
            let size = (try? file.resourceValues(forKeys: [.fileSizeKey]))?.fileSize ?? 0
            if size > 256 * 1024 {
                let previous = directory.appendingPathComponent("update.previous.log")
                try? fm.removeItem(at: previous)
                try? fm.moveItem(at: file, to: previous)
            }
        } catch {
            NSLog("Harness Portable update log directory unavailable (code=%ld)", (error as NSError).code)
            return
        }
        let descriptor = open(file.path, O_WRONLY | O_APPEND | O_CREAT | O_NOFOLLOW, 0o600)
        guard descriptor >= 0 else {
            NSLog("Harness Portable update log could not be opened (errno=%d)", errno)
            return
        }
        defer { close(descriptor) }
        let timestamp = ISO8601DateFormatter().string(from: Date())
        let line = Data("\(timestamp) pid=\(getpid()) \(event) \(fields)\n".utf8)
        line.withUnsafeBytes { buffer in _ = write(descriptor, buffer.baseAddress, buffer.count) }
    }

    static func failure(_ event: String, _ error: Error) {
        // Localized errors can include download URLs or filesystem paths.
        let underlying = error as NSError
        let domain = underlying.domain.prefix(96).filter { $0.isLetter || $0.isNumber || $0 == "." || $0 == "_" }
        record(event, "domain=\(domain) code=\(underlying.code)")
    }

    static func version(_ value: String) -> String {
        value.range(of: #"^[0-9.]{1,24}$"#, options: .regularExpression) == nil ? "invalid" : value
    }
}
