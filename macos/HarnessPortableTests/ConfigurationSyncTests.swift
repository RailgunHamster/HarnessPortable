import XCTest
import Foundation
import Network
@testable import HarnessPortable

@MainActor
final class ConfigurationSyncTests: XCTestCase {
    private func fixture() throws -> [String: Any] {
        let url = Bundle(for: ConfigurationSyncTests.self).url(forResource: "shared-configuration-v1", withExtension: "json")!
        return try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as! [String: Any]
    }
    func testSharedWireFixtureAndCredentialAllowlist() throws {
        let doc = try fixture(); try SharedConfiguration.validate(doc)
        XCTAssertEqual(SharedConfiguration.entries(doc)["shared:appearance"] as? String, "dark")
        for field in ["password", "identityFile", "authInput", "webdavPassword"] {
            var value = doc; var entries = SharedConfiguration.entries(value); var tunnel = entries["tunnel:shared-fixture-1"] as! [String: Any]
            tunnel[field] = "local-secret"; entries["tunnel:shared-fixture-1"] = tunnel; value["entries"] = entries
            XCTAssertThrowsError(try SharedConfiguration.validate(value))
        }
    }
    func testIndependentMergeAndDeleteEditConflictChoices() throws {
        let base = try fixture(); var local = base; var remote = base
        var l = SharedConfiguration.entries(local), r = SharedConfiguration.entries(remote)
        l["shared:appearance"] = "light"; r["windows:closeBehavior"] = "exit"; local["entries"] = l; remote["entries"] = r
        let merged = try SharedConfiguration.merge(base: base, local: local, remote: remote)
        XCTAssertTrue(merged.conflicts.isEmpty); XCTAssertEqual(SharedConfiguration.entries(merged.document)["shared:appearance"] as? String, "light")
        l.removeValue(forKey: "tunnel:shared-fixture-1"); var tunnel = r["tunnel:shared-fixture-1"] as! [String: Any]; tunnel["name"] = "云端修改"; r["tunnel:shared-fixture-1"] = tunnel
        local["entries"] = l; remote["entries"] = r
        XCTAssertTrue(try SharedConfiguration.merge(base: base, local: local, remote: remote).conflicts.contains("tunnel:shared-fixture-1"))
        XCTAssertNil(SharedConfiguration.entries(try SharedConfiguration.merge(base: base, local: local, remote: remote, choice: "local").document)["tunnel:shared-fixture-1"])
        XCTAssertNotNil(SharedConfiguration.entries(try SharedConfiguration.merge(base: base, local: local, remote: remote, choice: "remote").document)["tunnel:shared-fixture-1"])
    }
    func testReservedVaultIdentityAndUnsupportedVersionsFailClosed() throws {
        var doc = try fixture(); var entries = SharedConfiguration.entries(doc); var tunnel = entries.removeValue(forKey: "tunnel:shared-fixture-1") as! [String: Any]
        tunnel["id"] = "#webdav-sync"; entries["tunnel:#webdav-sync"] = tunnel; doc["entries"] = entries
        XCTAssertThrowsError(try SharedConfiguration.validate(doc))
        doc = try fixture(); doc["version"] = 2; XCTAssertThrowsError(try SharedConfiguration.validate(doc))
        doc = try fixture(); entries = SharedConfiguration.entries(doc); entries["android:browserFullscreen"] = "true"; doc["entries"] = entries; XCTAssertThrowsError(try SharedConfiguration.validate(doc))
    }
    func testUrlPrivacyAndExplicitHttpOptIn() throws {
        XCTAssertEqual(try SharedConfiguration.portableURL("https://name:secret@fixture.invalid/page?token=secret#private"), "https://fixture.invalid/page")
        XCTAssertEqual(try SharedConfiguration.portableURL("https://fixture.invalid/a%2Fb"), "https://fixture.invalid/a%2Fb")
        XCTAssertThrowsError(try WebDavConfigurationClient.folder(WebDavOptions(url: "http://127.0.0.1/dav/")))
        XCTAssertThrowsError(try WebDavConfigurationClient.folder(WebDavOptions(url: "https://name:secret@fixture.invalid/dav/")))
    }
    func testRealLoopbackConditionalReadWriteAndWeakEtags() async throws {
        let cloud = try await SyncLoopback.start(); defer { cloud.close() }
        let options = WebDavOptions(url: cloud.url, username: "同步用户", allowHttp: true); let client = WebDavConfigurationClient(); client.begin()
        try await client.test(options, password: "同步密码")
        let missing = try await client.read(options, password: "同步密码"); XCTAssertTrue(missing.missing)
        let created = try await client.write(options, password: "同步密码", document: fixture(), etag: nil, missing: true); XCTAssertTrue(created)
        let loaded = try await client.read(options, password: "同步密码"); XCTAssertFalse(loaded.missing)
        let replaced = try await client.write(options, password: "同步密码", document: fixture(), etag: loaded.etag, missing: false); XCTAssertTrue(replaced)
        for etag in ["*", "W/\"weak\"", "\"a\", \"b\""] {
            do { _ = try await client.write(options, password: "secret", document: fixture(), etag: etag, missing: false); XCTFail("Invalid ETag caused an upload") } catch { }
        }
        let requests = cloud.requests()
        XCTAssertEqual(requests.first?.method, "PROPFIND"); XCTAssertEqual(requests.first?.headers["depth"], "0")
        XCTAssertTrue(requests.contains { $0.method == "PUT" && $0.headers["if-none-match"] == "*" })
        XCTAssertTrue(requests.contains { $0.method == "PUT" && $0.headers["if-match"] == loaded.etag })
        XCTAssertFalse(String(data: cloud.document()!, encoding: .utf8)!.contains("同步密码"))
    }
    func testRealMacAdapterKeepsCredentialsAndLocalPathsOffTheCloud() async throws {
        let cloud = try await SyncLoopback.start(); defer { cloud.close() }
        let root = FileManager.default.temporaryDirectory.appendingPathComponent("HarnessSync-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true); defer { try? FileManager.default.removeItem(at: root) }
        let profiles = ProfileStore(path: root.appendingPathComponent("profiles.json")); let settings = SettingsStore(path: root.appendingPathComponent("settings.json")); let vault = KeychainStore()
        let id = "fixture-" + UUID().uuidString; let p = TunnelProfile(id: id, name: "测试", sshHost: "fixture.invalid", user: "fixture", identityFile: "/private/fixture-key")
        try profiles.replaceForSync(ProfileConfig(tunnels: [p])); try vault.setPassword("local-ssh-secret", for: id); defer { vault.deletePassword(for: id) }
        let sync = ConfigurationSync(profiles: profiles, settings: settings, keychain: vault, directory: root, secretPrefix: "#sync-test-\(UUID().uuidString):")
        let options = WebDavOptions(url: cloud.url, username: "fixture", allowHttp: true)
        try sync.configure(options, password: "dav-secret"); defer { try? sync.configure(options, clearPassword: true) }
        await sync.run(); XCTAssertEqual(sync.phase, "success", sync.message)
        XCTAssertEqual(vault.password(for: id), "local-ssh-secret"); XCTAssertEqual(profiles.tunnels.first?.identityFile, "/private/fixture-key")
        let body = String(data: cloud.document()!, encoding: .utf8)!
        for secret in ["local-ssh-secret", "dav-secret", "identityFile", "/private/"] { XCTAssertFalse(body.contains(secret)) }
    }
}

private final class SyncLoopback {
    struct Request { let method: String; let headers: [String: String]; let body: Data }
    private let listener: NWListener
    private let queue = DispatchQueue(label: "harness.sync.fixture")
    private let lock = NSLock()
    private var body: Data?
    private var revision = 0
    private var records: [Request] = []
    private var connections: [NWConnection] = []
    private(set) var url = ""
    private init() throws {
        let parameters = NWParameters.tcp; parameters.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: .any)
        listener = try NWListener(using: parameters)
    }
    static func start() async throws -> SyncLoopback {
        let server = try SyncLoopback()
        try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<Void, Error>) in
            var completed = false
            server.listener.stateUpdateHandler = { state in
                if completed { return }
                switch state { case .ready: completed = true; server.url = "http://127.0.0.1:\(server.listener.port!.rawValue)/dav/Harness/"; continuation.resume()
                case .failed(let error): completed = true; continuation.resume(throwing: error); default: break }
            }
            server.listener.newConnectionHandler = { connection in
                server.lock.lock(); server.connections.append(connection); server.lock.unlock()
                connection.start(queue: server.queue); server.receive(connection, buffer: Data())
            }
            server.listener.start(queue: server.queue)
        }
        return server
    }
    func requests() -> [Request] { lock.lock(); defer { lock.unlock() }; return records }
    func document() -> Data? { lock.lock(); defer { lock.unlock() }; return body }
    func close() { listener.cancel(); lock.lock(); let all = connections; lock.unlock(); all.forEach { $0.cancel() } }
    private func receive(_ connection: NWConnection, buffer: Data) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self] data, _, complete, error in
            guard let self, error == nil else { connection.cancel(); return }
            var accumulated = buffer; if let data { accumulated.append(data) }
            guard accumulated.count <= 2 * 1024 * 1024 else { connection.cancel(); return }
            if let end = accumulated.range(of: Data("\r\n\r\n".utf8)), let head = String(data: accumulated[..<end.lowerBound], encoding: .utf8) {
                let lines = head.components(separatedBy: "\r\n"); var headers: [String: String] = [:]
                for line in lines.dropFirst() { if let i = line.firstIndex(of: ":") { headers[String(line[..<i]).lowercased()] = String(line[line.index(after: i)...]).trimmingCharacters(in: .whitespaces) } }
                let count = Int(headers["content-length"] ?? "0") ?? 0
                if accumulated.count >= end.upperBound + count {
                    let request = Request(method: lines[0].components(separatedBy: " ")[0], headers: headers, body: Data(accumulated[end.upperBound..<(end.upperBound + count)]))
                    self.respond(connection, request); return
                }
            }
            if complete { connection.cancel() } else { self.receive(connection, buffer: accumulated) }
        }
    }
    private func respond(_ connection: NWConnection, _ request: Request) {
        lock.lock(); records.append(request); let status: Int; var payload = Data()
        switch request.method {
        case "PROPFIND": status = 207
        case "GET": status = body == nil ? 404 : 200; payload = body ?? Data()
        case "PUT":
            let matched = body == nil ? request.headers["if-none-match"] == "*" : request.headers["if-match"] == "\"v\(revision)\""
            if matched { body = request.body; revision += 1; status = 204 } else { status = 412 }
        default: status = 405
        }
        let etag = "\"v\(revision)\""; lock.unlock()
        var response = Data("HTTP/1.1 \(status) Fixture\r\nContent-Type: application/json\r\nContent-Length: \(payload.count)\r\nETag: \(etag)\r\nConnection: close\r\n\r\n".utf8); response.append(payload)
        connection.send(content: response, completion: .contentProcessed { _ in connection.cancel() })
    }
}
