import Foundation
import Security

final class KeychainStore {
    private let service = "com.harness.portable"

    func hasPassword(for profileID: String) -> Bool {
        password(for: profileID) != nil
    }

    func password(for profileID: String) -> String? {
        var query = baseQuery(for: profileID)
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne

        var result: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &result) == errSecSuccess,
              let data = result as? Data else {
            return nil
        }
        return String(data: data, encoding: .utf8)
    }

    func setPassword(_ password: String, for profileID: String) throws {
        var query = baseQuery(for: profileID)
        _ = SecItemDelete(query as CFDictionary)

        query[kSecValueData as String] = Data(password.utf8)
        query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlock
        let status = SecItemAdd(query as CFDictionary, nil)
        guard status == errSecSuccess else {
            throw NSError(domain: NSOSStatusErrorDomain, code: Int(status), userInfo: [NSLocalizedDescriptionKey: "无法写入 macOS Keychain（\(status)）"])
        }
    }

    func deletePassword(for profileID: String) {
        _ = SecItemDelete(baseQuery(for: profileID) as CFDictionary)
    }

    private func baseQuery(for profileID: String) -> [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: profileID,
        ]
    }
}
