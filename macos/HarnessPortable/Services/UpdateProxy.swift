import Foundation
import CFNetwork

struct UpdateProxy: Equatable {
    static let defaultAddress = "localhost:7890"
    var enabled = false
    var address = defaultAddress

    init(enabled: Bool = false, address: String = defaultAddress) {
        self.enabled = enabled
        self.address = address.trimmingCharacters(in: .whitespacesAndNewlines)
        if self.address.isEmpty { self.address = Self.defaultAddress }
    }

    init(settings: AppSettings) {
        self.init(enabled: settings.updateProxyEnabled, address: settings.updateProxyAddress)
    }

    static func endpoint(_ address: String) -> (host: String, port: Int)? {
        let trimmed = address.trimmingCharacters(in: .whitespacesAndNewlines)
        let value = trimmed.contains("://") ? trimmed : "http://" + trimmed
        guard !value.contains(where: { $0.isWhitespace }),
              let parts = URLComponents(string: value), parts.scheme?.lowercased() == "http",
              let host = parts.host, !host.isEmpty, let port = parts.port, (1...65535).contains(port),
              parts.user == nil, parts.password == nil, parts.query == nil, parts.fragment == nil,
              parts.path.isEmpty || parts.path == "/" else { return nil }
        return (host.trimmingCharacters(in: CharacterSet(charactersIn: "[]")), port)
    }

    func configuration() throws -> URLSessionConfiguration {
        let config = URLSessionConfiguration.ephemeral
        // Setting every proxy mode explicitly prevents inherited system/PAC settings.
        var proxies: [AnyHashable: Any] = [
            kCFNetworkProxiesHTTPEnable as String: 0,
            kCFNetworkProxiesHTTPSEnable as String: 0,
            kCFNetworkProxiesSOCKSEnable as String: 0,
            kCFNetworkProxiesProxyAutoConfigEnable as String: 0,
            kCFNetworkProxiesProxyAutoDiscoveryEnable as String: 0,
            kCFNetworkProxiesExcludeSimpleHostnames as String: 0,
            kCFNetworkProxiesExceptionsList as String: [String](),
        ]
        if enabled {
            guard let endpoint = Self.endpoint(address) else {
                throw NSError(domain: "HarnessPortable.UpdateProxy", code: 1,
                    userInfo: [NSLocalizedDescriptionKey: "请输入 HTTP 代理地址，例如 localhost:7890。"])
            }
            proxies[kCFNetworkProxiesHTTPEnable as String] = 1
            proxies[kCFNetworkProxiesHTTPProxy as String] = endpoint.host
            proxies[kCFNetworkProxiesHTTPPort as String] = endpoint.port
            proxies[kCFNetworkProxiesHTTPSEnable as String] = 1
            proxies[kCFNetworkProxiesHTTPSProxy as String] = endpoint.host
            proxies[kCFNetworkProxiesHTTPSPort as String] = endpoint.port
        }
        config.connectionProxyDictionary = proxies
        config.timeoutIntervalForRequest = 20
        config.timeoutIntervalForResource = 300
        return config
    }
}
