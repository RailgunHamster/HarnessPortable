import Foundation
import Darwin

struct HostResolution {
    var ip: String
    var source: String
}

enum HostResolver {
    static func isIPLiteral(_ host: String) -> Bool {
        let value = host.trimmingCharacters(in: .whitespacesAndNewlines)
        if value.contains(":") { return true }
        let parts = value.split(separator: ".")
        guard parts.count == 4 else { return false }
        return parts.allSatisfy { Int($0).map { (0...255).contains($0) } ?? false }
    }

    static func isTailscaleAddress(_ ip: String) -> Bool {
        let parts = ip.split(separator: ".")
        guard parts.count == 4, let first = Int(parts[0]), let second = Int(parts[1]) else { return false }
        return first == 100 && (64...127).contains(second)
    }

    static func resolve(_ host: String) -> HostResolution? {
        let value = host.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { return nil }
        if isIPLiteral(value) { return HostResolution(ip: value, source: "ip") }

        var hints = addrinfo(
            ai_flags: AI_ADDRCONFIG,
            ai_family: AF_INET,
            ai_socktype: 0,
            ai_protocol: 0,
            ai_addrlen: 0,
            ai_canonname: nil,
            ai_addr: nil,
            ai_next: nil
        )
        var result: UnsafeMutablePointer<addrinfo>?
        let status = value.withCString { hostname in
            getaddrinfo(hostname, nil, &hints, &result)
        }
        guard status == 0, let first = result else { return nil }
        defer { freeaddrinfo(first) }

        var cursor: UnsafeMutablePointer<addrinfo>? = first
        while let item = cursor {
            if item.pointee.ai_family == AF_INET, let rawAddress = item.pointee.ai_addr {
                var address = rawAddress.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee.sin_addr }
                var buffer = [CChar](repeating: 0, count: Int(INET_ADDRSTRLEN))
                if inet_ntop(AF_INET, &address, &buffer, socklen_t(buffer.count)) != nil {
                    let ip = String(cString: buffer)
                    return HostResolution(ip: ip, source: isTailscaleAddress(ip) ? "tailscale" : "dns")
                }
            }
            cursor = item.pointee.ai_next
        }
        return nil
    }

    static func resolveURL(_ url: String) -> String? {
        guard var components = URLComponents(string: url),
              let host = components.host,
              !isIPLiteral(host),
              let resolution = resolve(host) else { return nil }
        components.host = resolution.ip
        return components.string
    }
}
