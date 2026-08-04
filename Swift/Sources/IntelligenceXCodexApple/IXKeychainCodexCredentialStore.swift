import Foundation
import IntelligenceXCodex
import Security

public enum IXKeychainCredentialAccessibility: Sendable {
    /// Credentials remain unavailable while the device is locked.
    case whenUnlockedThisDeviceOnly

    /// Credentials support background and companion experiences after the
    /// user has unlocked the device once following a restart.
    case afterFirstUnlockThisDeviceOnly

    var securityValue: CFString {
        switch self {
        case .whenUnlockedThisDeviceOnly:
            kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        case .afterFirstUnlockThisDeviceOnly:
            kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        }
    }
}

public actor IXKeychainCodexCredentialStore: IXCodexCredentialStoring {
    private let service: String
    private let account: String
    private let accessibility: IXKeychainCredentialAccessibility

    public init(
        service: String,
        account: String = "chatgpt-codex",
        accessibility: IXKeychainCredentialAccessibility = .whenUnlockedThisDeviceOnly
    ) {
        self.service = service
        self.account = account
        self.accessibility = accessibility
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
        try updateAccessibility(operation: "migrate")
        return try JSONDecoder().decode(IXCodexAuthBundle.self, from: data)
    }

    public func save(_ bundle: IXCodexAuthBundle) throws {
        let data = try JSONEncoder().encode(bundle)
        let update: [String: Any] = [
            kSecValueData as String: data,
            kSecAttrAccessible as String: accessibility.securityValue,
        ]
        let status = SecItemUpdate(baseQuery as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound {
            var insert = baseQuery
            insert[kSecValueData as String] = data
            insert[kSecAttrAccessible as String] = accessibility.securityValue
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

    private func updateAccessibility(operation: String) throws {
        let status = SecItemUpdate(
            baseQuery as CFDictionary,
            [kSecAttrAccessible as String: accessibility.securityValue] as CFDictionary
        )
        guard status == errSecSuccess else {
            throw IXKeychainError(operation: operation, status: status)
        }
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
