import Foundation
import IntelligenceXCodex
import Security

public actor IXKeychainCodexCredentialStore: IXCodexCredentialStoring {
    private let service: String
    private let account: String

    public init(service: String, account: String = "chatgpt-codex") {
        self.service = service
        self.account = account
    }

    public func load() throws -> IXCodexAuthBundle? {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound {
            return nil
        }
        guard status == errSecSuccess, let data = result as? Data else {
            throw IXKeychainError(operation: "read", status: status)
        }
        return try JSONDecoder().decode(IXCodexAuthBundle.self, from: data)
    }

    public func save(_ bundle: IXCodexAuthBundle) throws {
        let data = try JSONEncoder().encode(bundle)
        let update: [String: Any] = [
            kSecValueData as String: data,
            kSecAttrAccessible as String: kSecAttrAccessibleWhenUnlockedThisDeviceOnly,
        ]
        let status = SecItemUpdate(baseQuery as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound {
            var insert = baseQuery
            insert[kSecValueData as String] = data
            insert[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
            let insertStatus = SecItemAdd(insert as CFDictionary, nil)
            guard insertStatus == errSecSuccess else {
                throw IXKeychainError(operation: "save", status: insertStatus)
            }
            return
        }
        guard status == errSecSuccess else {
            throw IXKeychainError(operation: "update", status: status)
        }
    }

    public func delete() throws {
        let status = SecItemDelete(baseQuery as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else {
            throw IXKeychainError(operation: "delete", status: status)
        }
    }

    private var baseQuery: [String: Any] {
        [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecAttrSynchronizable as String: kCFBooleanFalse as Any,
        ]
    }
}

public struct IXKeychainError: LocalizedError, Sendable {
    public let operation: String
    public let status: OSStatus

    public init(operation: String, status: OSStatus) {
        self.operation = operation
        self.status = status
    }

    public var errorDescription: String? {
        let detail = SecCopyErrorMessageString(status, nil) as String? ?? "OSStatus \(status)"
        return "Unable to \(operation) ChatGPT credentials: \(detail)"
    }
}
