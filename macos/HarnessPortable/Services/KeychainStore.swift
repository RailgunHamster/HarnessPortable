import Foundation
import Security

final class KeychainStore {
    private let service = "com.harness.portable"
    private let cacheLock = NSLock()
    private var cachedPasswords: [String: String] = [:]  // keyed by Keychain account

    /// The SSH password uses the profile id as its Keychain account; the
    /// optional manual web-auth input lives under a suffixed account so both
    /// values share the service without colliding.
    private static func authInputAccount(for profileID: String) -> String {
        "\(profileID)#webauth"
    }

    func hasPassword(for profileID: String) -> Bool {
        password(for: profileID) != nil
    }

    func password(for profileID: String) -> String? {
        storedValue(for: profileID)
    }

    func setPassword(_ password: String, for profileID: String) throws {
        try setStoredValue(password, for: profileID)
    }

    func deletePassword(for profileID: String) {
        deleteStoredValue(for: profileID)
    }

    func hasAuthInput(for profileID: String) -> Bool {
        authInput(for: profileID) != nil
    }

    func authInput(for profileID: String) -> String? {
        storedValue(for: Self.authInputAccount(for: profileID))
    }

    func setAuthInput(_ value: String, for profileID: String) throws {
        try setStoredValue(value, for: Self.authInputAccount(for: profileID))
    }

    func deleteAuthInput(for profileID: String) {
        deleteStoredValue(for: Self.authInputAccount(for: profileID))
    }

    private func storedValue(for account: String) -> String? {
        cacheLock.lock()
        let cached = cachedPasswords[account]
        cacheLock.unlock()
        if let cached { return cached }

        var query = baseQuery(for: account)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        query[kSecUseAuthenticationUI as String] = kSecUseAuthenticationUIFail

        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data,
              let value = String(data: data, encoding: .utf8) else {
            return nil
        }
        cacheLock.lock()
        cachedPasswords[account] = value
        cacheLock.unlock()
        return value
    }

    private func setStoredValue(_ value: String, for account: String) throws {
        var query = baseQuery(for: account)
        _ = SecItemDelete(query as CFDictionary)

        query[kSecValueData as String] = Data(value.utf8)
        let status = SecItemAdd(query as CFDictionary, nil)
        guard status == errSecSuccess else {
            throw NSError(domain: NSOSStatusErrorDomain, code: Int(status), userInfo: [NSLocalizedDescriptionKey: "无法写入 macOS Keychain（\(status)）"])
        }
        cacheLock.lock()
        cachedPasswords[account] = value
        cacheLock.unlock()
    }

    private func deleteStoredValue(for account: String) {
        _ = SecItemDelete(baseQuery(for: account) as CFDictionary)
        cacheLock.lock()
        cachedPasswords.removeValue(forKey: account)
        cacheLock.unlock()
    }

    private func baseQuery(for account: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
    }
}
