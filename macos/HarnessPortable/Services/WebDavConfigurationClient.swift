import Foundation

struct WebDavOptions: Codable, Equatable {
    var url = ""
    var username = ""
    var autoSync = false
    var allowHttp = false
}

private final class SyncRedirectPolicy: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
}

final class WebDavConfigurationClient {
    private let policy = SyncRedirectPolicy()
    private lazy var session: URLSession = {
        let c = URLSessionConfiguration.ephemeral; c.timeoutIntervalForRequest = 15; c.timeoutIntervalForResource = 25
        c.urlCache = nil; c.httpCookieStorage = nil; c.urlCredentialStorage = nil
        return URLSession(configuration: c, delegate: policy, delegateQueue: nil)
    }()
    private let lock = NSLock()
    private var cancelled = false
    func begin() { lock.lock(); cancelled = false; lock.unlock() }
    func cancel() { lock.lock(); cancelled = true; lock.unlock(); session.getAllTasks { $0.forEach { $0.cancel() } } }
    func checkCancelled() throws { lock.lock(); let value = cancelled; lock.unlock(); if value { throw CancellationError() } }
    static func folder(_ options: WebDavOptions) throws -> URL {
        guard let c = URLComponents(string: options.url), let scheme = c.scheme, ["https", "http"].contains(scheme), !(c.host ?? "").isEmpty,
              c.user == nil, c.password == nil, c.query == nil, c.fragment == nil, scheme == "https" || options.allowHttp,
              !options.username.contains(":"), !options.username.unicodeScalars.contains(where: CharacterSet.controlCharacters.contains),
              let url = URL(string: options.url.trimmingCharacters(in: CharacterSet(charactersIn: "/")) + "/") else { throw SyncError("请填写 HTTPS WebDAV 目录；可信局域网 HTTP 需明确允许，地址不能含账号、密码或查询参数。") }
        return url
    }
    private func request(_ options: WebDavOptions, _ password: String, method: String, directory: Bool = false) throws -> URLRequest {
        try checkCancelled(); let root = try Self.folder(options)
        var request = URLRequest(url: directory ? root : root.appendingPathComponent(SharedConfiguration.filename)); request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if !options.username.isEmpty || !password.isEmpty { request.setValue("Basic " + Data((options.username + ":" + password).utf8).base64EncodedString(), forHTTPHeaderField: "Authorization") }
        return request
    }
    private func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        try checkCancelled(); let (bytes, response) = try await session.bytes(for: request)
        guard let response = response as? HTTPURLResponse else { throw SyncError("WebDAV 响应无效") }
        guard response.expectedContentLength <= SharedConfiguration.limit else { throw SyncError("同步响应超过 1 MiB") }
        var data = Data()
        for try await byte in bytes { try checkCancelled(); guard data.count < SharedConfiguration.limit else { throw SyncError("同步响应超过 1 MiB") }; data.append(byte) }
        return (data, response)
    }
    private func check(_ code: Int) throws {
        if (200...299).contains(code) { return }
        let message: String
        switch code { case 401: message = "WebDAV 登录失败（401），请检查同步账号。"; case 403: message = "WebDAV 拒绝访问（403），请检查目录权限。"; case 404, 409: message = "WebDAV 目录不存在，请先创建私有同步目录。"; case 300...399: message = "WebDAV 返回重定向，请填写最终地址；未转发凭据。"; default: message = "WebDAV 请求失败（HTTP \(code)）。" }
        throw SyncError(message)
    }
    func test(_ options: WebDavOptions, password: String) async throws {
        var req = try request(options, password, method: "PROPFIND", directory: true); req.setValue("0", forHTTPHeaderField: "Depth")
        let (_, response) = try await send(req); try check(response.statusCode)
    }
    struct Remote { let document: [String: Any]; let etag: String?; let missing: Bool }
    func read(_ options: WebDavOptions, password: String) async throws -> Remote {
        let (data, response) = try await send(request(options, password, method: "GET"))
        if response.statusCode == 404 { return Remote(document: SharedConfiguration.empty(), etag: nil, missing: true) }
        try check(response.statusCode)
        guard let doc = try JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw SyncError("同步文件不是 JSON 对象") }
        try SharedConfiguration.validate(doc); return Remote(document: doc, etag: response.value(forHTTPHeaderField: "ETag"), missing: false)
    }
    func write(_ options: WebDavOptions, password: String, document: [String: Any], etag: String?, missing: Bool) async throws -> Bool {
        try SharedConfiguration.validate(document)
        guard missing || etag?.range(of: "^\"[!#-~\\u0080-\\uFFFF]*\"$", options: .regularExpression) != nil else { throw SyncError("服务器未提供强 ETag，已停止上传，避免覆盖其他设备的修改。") }
        let data = try JSONSerialization.data(withJSONObject: document, options: [.sortedKeys]); guard data.count <= SharedConfiguration.limit else { throw SyncError("同步文件超过 1 MiB") }
        var req = try request(options, password, method: "PUT"); req.httpBody = data
        req.setValue(missing ? "*" : etag!, forHTTPHeaderField: missing ? "If-None-Match" : "If-Match"); req.setValue("application/json; charset=utf-8", forHTTPHeaderField: "Content-Type")
        let (_, response) = try await send(req); if response.statusCode == 412 { return false }; try check(response.statusCode); return true
    }
}
