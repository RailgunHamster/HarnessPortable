import XCTest
import Network
import CFNetwork
import CryptoKit
@testable import HarnessPortable

final class UpdateProxyTests: XCTestCase {
    func testLegacyDefaultAndPreferenceRoundTrip() throws {
        var settings = try JSONDecoder().decode(AppSettings.self, from: Data("{}".utf8))
        XCTAssertFalse(settings.updateProxyEnabled)
        XCTAssertEqual(settings.updateProxyAddress, "localhost:7890")
        settings.updateProxyEnabled = true; settings.updateProxyAddress = "127.0.0.1:8080"
        let restored = try JSONDecoder().decode(AppSettings.self, from: JSONEncoder().encode(settings))
        XCTAssertEqual(UpdateProxy(settings: settings), UpdateProxy(settings: restored))
    }

    func testAddressValidation() {
        for value in ["localhost:7890", "http://127.0.0.1:80/", "[::1]:7890"] {
            XCTAssertNotNil(UpdateProxy.endpoint(value), value)
        }
        for value in ["localhost", "localhost:0", "localhost:65536", "https://localhost:7890", "socks5://localhost:7890",
                      "http://user:pass@localhost:7890", "localhost:7890/path", "localhost:7890?x=1", "local host:7890"] {
            XCTAssertNil(UpdateProxy.endpoint(value), value)
        }
    }

    func testConfigurationExplicitlyDisablesSystemProxies() throws {
        let config = try UpdateProxy(enabled: false, address: "invalid address").configuration()
        let values = try XCTUnwrap(config.connectionProxyDictionary)
        for key in [kCFNetworkProxiesHTTPEnable, kCFNetworkProxiesHTTPSEnable, kCFNetworkProxiesSOCKSEnable,
                    kCFNetworkProxiesProxyAutoConfigEnable, kCFNetworkProxiesProxyAutoDiscoveryEnable] {
            XCTAssertEqual(values[key as String] as? Int, 0)
        }
        let active = try UpdateProxy(enabled: true).configuration().connectionProxyDictionary
        XCTAssertEqual(active?[kCFNetworkProxiesHTTPProxy as String] as? String, "localhost")
        XCTAssertEqual(active?[kCFNetworkProxiesHTTPSPort as String] as? Int, 7890)
    }

    func testProxyCarriesFeedDownloadAndRedirect() async throws {
        let ready = expectation(description: "proxy ready")
        let server = try ProxyFixture(ready: ready)
        defer { server.stop() }
        await fulfillment(of: [ready], timeout: 5)
        let proxy = UpdateProxy(enabled: true, address: "127.0.0.1:\(try XCTUnwrap(server.port))")
        let session = URLSession(configuration: try proxy.configuration())
        defer { session.invalidateAndCancel() }
        let (data, _) = try await session.data(from: URL(string: "http://update.invalid/macos.json")!)
        let feed = try JSONDecoder().decode(MacUpdateFeed.self, from: data)
        XCTAssertEqual(feed.version, "9.0.0")
        let (file, _) = try await session.download(from: URL(string: "http://update.invalid/\(feed.dmg)")!)
        defer { try? FileManager.default.removeItem(at: file) }
        XCTAssertEqual(try String(contentsOf: file), "image")
        XCTAssertTrue(server.requests.contains(where: { $0.hasPrefix("GET http://download.invalid/final") }))
    }

    func testDisabledProxyConnectsDirectly() async throws {
        let ready = expectation(description: "origin ready")
        let server = try ProxyFixture(ready: ready)
        defer { server.stop() }
        await fulfillment(of: [ready], timeout: 5)
        let session = URLSession(configuration: try UpdateProxy(enabled: false, address: "bad address").configuration())
        defer { session.invalidateAndCancel() }
        let (data, _) = try await session.data(from: URL(string: "http://127.0.0.1:\(try XCTUnwrap(server.port))/direct")!)
        XCTAssertEqual(String(decoding: data, as: UTF8.self), "image")
        XCTAssertTrue(server.requests.contains(where: { $0.hasPrefix("GET /direct ") }))
    }

    func testHttpsConnectUsesProxy() async throws {
        let ready = expectation(description: "CONNECT proxy ready")
        let server = try ProxyFixture(ready: ready)
        defer { server.stop() }
        await fulfillment(of: [ready], timeout: 5)
        let config = try UpdateProxy(enabled: true, address: "127.0.0.1:\(try XCTUnwrap(server.port))").configuration()
        config.timeoutIntervalForResource = 5
        let session = URLSession(configuration: config)
        defer { session.invalidateAndCancel() }
        do { _ = try await session.data(from: URL(string: "https://update.invalid/macos.json")!); XCTFail("Proxy should reject the test tunnel") }
        catch { }
        XCTAssertTrue(server.requests.contains(where: { $0.hasPrefix("CONNECT update.invalid:443 ") }))
    }

    @MainActor
    func testAbsoluteDownloadURLHandsOffTheExactVerifiedFileEvenWhenItsNameAlreadyExists() async throws {
        let ready = expectation(description: "update feed ready")
        let server = try ProxyFixture(ready: ready, absoluteFeed: true)
        defer { server.stop() }
        await fulfillment(of: [ready], timeout: 5)
        let base = URL(string: "http://127.0.0.1:\(try XCTUnwrap(server.port))")!
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: false)
        defer { try? FileManager.default.removeItem(at: directory) }
        let preexisting = directory.appendingPathComponent("official.dmg")
        try Data("earlier file".utf8).write(to: preexisting)
        var opened: URL?
        let settings = SettingsStore(path: directory.appendingPathComponent("settings.json"))
        let update = UpdateService(settings: settings, feedBaseOverride: base, downloadFolder: directory,
            openImage: { opened = $0; return true })
        update.check()
        try await waitForUpdate(update, .available(version: "9.0.0"))
        update.download()
        try await waitForUpdate(update, .readyToInstall(version: "9.0.0"))
        update.openInstaller()
        let actual = try XCTUnwrap(opened)
        XCTAssertEqual(actual.deletingLastPathComponent().path, directory.path)
        XCTAssertNotEqual(actual, preexisting)
        XCTAssertEqual(try String(contentsOf: actual), "image")
        XCTAssertEqual(try String(contentsOf: preexisting), "earlier file")
        XCTAssertFalse(update.canInstallAutomatically)
    }

    @MainActor
    private func waitForUpdate(_ update: UpdateService, _ expected: UpdatePhase) async throws {
        let deadline = ProcessInfo.processInfo.systemUptime + 8
        while update.phase != expected && update.isBusy && ProcessInfo.processInfo.systemUptime < deadline {
            try await Task.sleep(nanoseconds: 50_000_000)
        }
        XCTAssertEqual(update.phase, expected)
    }
}

private final class ProxyFixture: @unchecked Sendable {
    private let listener: NWListener
    private let queue = DispatchQueue(label: "HarnessPortable.ProxyTest")
    private let lock = NSLock()
    private var recorded: [String] = []
    private let absoluteFeed: Bool
    var port: UInt16? { listener.port?.rawValue }
    var requests: [String] { lock.lock(); defer { lock.unlock() }; return recorded }
    init(ready: XCTestExpectation, absoluteFeed: Bool = false) throws {
        self.absoluteFeed = absoluteFeed
        let parameters = NWParameters.tcp
        parameters.requiredLocalEndpoint = .hostPort(host: "127.0.0.1", port: .any)
        listener = try NWListener(using: parameters)
        listener.stateUpdateHandler = { state in if case .ready = state { ready.fulfill() } }
        listener.newConnectionHandler = { [weak self] connection in
            connection.start(queue: self?.queue ?? .global())
            self?.read(connection, buffer: Data())
        }
        listener.start(queue: queue)
    }
    func stop() { listener.cancel() }
    private func read(_ connection: NWConnection, buffer: Data) {
        connection.receive(minimumIncompleteLength: 1, maximumLength: 65536) { [weak self] data, _, complete, error in
            guard let self else { connection.cancel(); return }
            var buffer = buffer; buffer.append(data ?? Data())
            let text = String(decoding: buffer, as: UTF8.self)
            guard text.contains("\r\n\r\n") else {
                if complete || error != nil { connection.cancel() } else { self.read(connection, buffer: buffer) }
                return
            }
            let request = String(text.components(separatedBy: "\r\n").first ?? "")
            self.lock.lock(); self.recorded.append(request); self.lock.unlock()
            let code: Int, headers: String, body: String
            if request.hasPrefix("CONNECT ") { code = 502; headers = ""; body = "refused test tunnel" }
            else if request.contains("macos.json") {
                code = 200; headers = ""
                if self.absoluteFeed, let port = self.port {
                    let digest = SHA256.hash(data: Data("image".utf8)).map { String(format: "%02x", $0) }.joined()
                    body = #"{"version":"9.0.0","dmg":"http://127.0.0.1:\#(port)/downloads/official.dmg","sha256":"\#(digest)"}"#
                } else {
                    body = #"{"version":"9.0.0","dmg":"test.dmg"}"#
                }
            }
            else if request.contains("test.dmg") { code = 302; headers = "Location: http://download.invalid/final\r\n"; body = "" }
            else { code = 200; headers = ""; body = "image" }
            let reply = "HTTP/1.1 \(code) Test\r\n\(headers)Content-Length: \(body.utf8.count)\r\nConnection: close\r\n\r\n\(body)"
            connection.send(content: Data(reply.utf8), completion: .contentProcessed { _ in connection.cancel() })
        }
    }
}
