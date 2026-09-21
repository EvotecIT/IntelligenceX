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
        try snapshot().validatedBundle()
    }

    public func save(_ bundle: IXCodexAuthBundle) throws { try write(bundle) }

    // Retain a token-free tombstone so a delete/recreate cycle cannot reuse an
    // earlier absent revision. Removing credentials never removes their epoch.
    public func delete() throws { try write(nil) }

    private func write(_ bundle: IXCodexAuthBundle?) throws {
        let update: [String: Any] = [
            kSecValueData as String: try encoded(bundle),
            kSecAttrGeneric as String: revision(),
            kSecAttrAccessible as String: accessibility.securityValue,
        ]
        var status = SecItemUpdate(baseQuery as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound {
            status = SecItemAdd(baseQuery.merging(update) { _, new in new } as CFDictionary, nil)
            if status == errSecDuplicateItem {
                status = SecItemUpdate(baseQuery as CFDictionary, update as CFDictionary)
            }
        }
        guard status == errSecSuccess else { throw IXKeychainError(operation: "write", status: status) }
    }

    public func replace(
        _ expected: IXCodexCredentialSnapshot, with replacement: IXCodexAuthBundle?
    ) throws -> IXCodexCredentialSnapshot? {
        let nextRevision = revision()
        let update: [String: Any] = [
            kSecValueData as String: try encoded(replacement),
            kSecAttrGeneric as String: nextRevision,
            kSecAttrAccessible as String: accessibility.securityValue,
        ]
        let status: OSStatus
        if let expectedRevision = expected.revision {
            var query = baseQuery
            query[kSecAttrGeneric as String] = expectedRevision
            status = SecItemUpdate(query as CFDictionary, update as CFDictionary)
            if status == errSecItemNotFound { return nil }
        } else {
            status = SecItemAdd(baseQuery.merging(update) { _, new in new } as CFDictionary, nil)
            if status == errSecDuplicateItem { return nil }
        }
        guard status == errSecSuccess else { throw IXKeychainError(operation: "replace", status: status) }
        return .init(bundle: replacement, revision: nextRevision)
    }

    private func revision() -> Data { Data(UUID().uuidString.utf8) }
    private func encoded(_ bundle: IXCodexAuthBundle?) throws -> Data {
        try bundle.map { try JSONEncoder().encode($0) } ?? Data("null".utf8)
    }

    public func snapshot() throws -> IXCodexCredentialSnapshot {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecReturnAttributes as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        if status == errSecItemNotFound { return .init(bundle: nil, revision: nil) }
        guard status == errSecSuccess,
              let item = result as? [String: Any],
              let data = item[kSecValueData as String] as? Data else {
            throw IXKeychainError(operation: "read revision", status: status)
        }
        if let revision = item[kSecAttrGeneric as String] as? Data, !revision.isEmpty {
            try updateAccessibility(operation: "migrate")
            if data == Data("null".utf8) { return .init(bundle: nil, revision: revision) }
            let bundle = try? JSONDecoder().decode(IXCodexAuthBundle.self, from: data)
            return .init(bundle: bundle, revision: revision, isUnreadable: bundle == nil)
        }
        // Metadata-only migration invalidates older observations without ever
        // restoring token bytes captured before a concurrent write.
        let migration = SecItemUpdate(baseQuery as CFDictionary,
            [kSecAttrGeneric as String: revision()] as CFDictionary)
        if migration == errSecItemNotFound { return .init(bundle: nil, revision: nil) }
        guard migration == errSecSuccess else { throw IXKeychainError(operation: "migrate revision", status: migration) }
        return try snapshot()
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
