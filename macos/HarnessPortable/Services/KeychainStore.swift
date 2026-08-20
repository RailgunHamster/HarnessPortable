import Foundation
import Security

final class KeychainStore {
    private let service = "com.harness.portable"
    private let cacheLock = NSLock()
    private var cachedPasswords: [String: String] = [:]

    func hasPassword(for profileID: String) -> Bool {
        password(for: profileID) != nil
    }

    func password(for profileID: String) -> String? {
        cacheLock.lock()
        let cached = cachedPasswords[profileID]
        cacheLock.unlock()
        if let cached { return cached }

        var query = baseQuery(for: profileID)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        query[kSecUseAuthenticationUI as String] = kSecUseAuthenticationUIFail

        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data,
              let password = String(data: data, encoding: .utf8) else {
            return nil
        }
        cacheLock.lock()
        cachedPasswords[profileID] = password
        cacheLock.unlock()
        return password
    }

    func setPassword(_ password: String, for profileID: String) throws {
        var query = baseQuery(for: profileID)
        _ = SecItemDelete(query as CFDictionary)

        query[kSecValueData as String] = Data(password.utf8)
        let status = SecItemAdd(query as CFDictionary, nil)
        guard status == errSecSuccess else {
            throw NSError(domain: NSOSStatusErrorDomain, code: Int(status), userInfo: [NSLocalizedDescriptionKey: "无法写入 macOS Keychain（\(status)）"])
        }
        cacheLock.lock()
        cachedPasswords[profileID] = password
        cacheLock.unlock()
    }

    func deletePassword(for profileID: String) {
        _ = SecItemDelete(baseQuery(for: profileID) as CFDictionary)
        cacheLock.lock()
        cachedPasswords.removeValue(forKey: profileID)
        cacheLock.unlock()
    }

    private func baseQuery(for profileID: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: profileID,
        ]
    }
}
