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
            kSecAttrGeneric as String: revision(),
            kSecAttrAccessible as String: accessibility.securityValue,
        ]
        let status = SecItemUpdate(baseQuery as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound {
            var insert = baseQuery
            insert[kSecValueData as String] = data
            insert[kSecAttrGeneric as String] = revision()
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

    /// The revision attribute makes the Keychain update/delete query itself
    /// conditional, including when another actor or process writes this item.
    public func replace(
        _ expected: IXCodexAuthBundle?, with replacement: IXCodexAuthBundle?
    ) throws -> Bool {
        guard let current = try versionedItem() else {
            guard expected == nil else { return false }
            guard let replacement else { return true }
            var insert = baseQuery
            insert[kSecValueData as String] = try JSONEncoder().encode(replacement)
            insert[kSecAttrGeneric as String] = revision()
            insert[kSecAttrAccessible as String] = accessibility.securityValue
            let status = SecItemAdd(insert as CFDictionary, nil)
            if status == errSecDuplicateItem { return false }
            guard status == errSecSuccess else { throw IXKeychainError(operation: "insert", status: status) }
            return true
        }
        guard current.bundle == expected else { return false }
        var query = baseQuery
        query[kSecAttrGeneric as String] = current.revision
        let status: OSStatus
        if let replacement {
            status = SecItemUpdate(query as CFDictionary, [
                kSecValueData as String: try JSONEncoder().encode(replacement),
                kSecAttrGeneric as String: revision(),
                kSecAttrAccessible as String: accessibility.securityValue,
            ] as CFDictionary)
        } else {
            status = SecItemDelete(query as CFDictionary)
        }
        if status == errSecItemNotFound { return false }
        guard status == errSecSuccess else { throw IXKeychainError(operation: "replace", status: status) }
        return true
    }

    private func revision() -> Data { Data(UUID().uuidString.utf8) }

    private func versionedItem() throws -> (bundle: IXCodexAuthBundle, revision: Data)? {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecReturnAttributes as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess,
              let item = result as? [String: Any],
              let data = item[kSecValueData as String] as? Data else {
            throw IXKeychainError(operation: "read revision", status: status)
        }
        if let revision = item[kSecAttrGeneric as String] as? Data, !revision.isEmpty {
            return (try JSONDecoder().decode(IXCodexAuthBundle.self, from: data), revision)
        }
        // Legacy entries have no revision. Change metadata only, then re-read
        // both the credentials and revision. A concurrent writer's credentials
        // must never be copied back from the pre-migration read.
        let migration = SecItemUpdate(baseQuery as CFDictionary,
            [kSecAttrGeneric as String: revision()] as CFDictionary)
        if migration == errSecItemNotFound { return nil }
        guard migration == errSecSuccess else { throw IXKeychainError(operation: "migrate revision", status: migration) }
        return try versionedItem()
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
