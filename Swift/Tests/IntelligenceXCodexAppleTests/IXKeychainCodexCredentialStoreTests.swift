import Foundation
import IntelligenceXCodex
import Security
import Testing
@testable import IntelligenceXCodexApple

@Test func keychainAccessibilityPoliciesMapToDeviceOnlySecurityClasses() {
    #expect(
        IXKeychainCredentialAccessibility.whenUnlockedThisDeviceOnly.securityValue
            == kSecAttrAccessibleWhenUnlockedThisDeviceOnly
    )
    #expect(
        IXKeychainCredentialAccessibility.afterFirstUnlockThisDeviceOnly.securityValue
            == kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
    )
}

@Test func keychainConditionalReplacementMigratesLegacyAndPreservesOtherWriters() async throws {
    let service = "IntelligenceX.CAS.Tests." + UUID().uuidString
    let base: [String: Any] = [kSecClass as String: kSecClassGenericPassword,
        kSecAttrService as String: service, kSecAttrAccount as String: "chatgpt-codex",
        kSecAttrSynchronizable as String: kCFBooleanFalse as Any]
    defer { SecItemDelete(base as CFDictionary) }
    let original = IXCodexAuthBundle(accessToken: "old", refreshToken: "refresh")
    let fresh = IXCodexAuthBundle(accessToken: "fresh", refreshToken: "refresh")
    let other = IXCodexAuthBundle(accessToken: "other", refreshToken: "replacement")
    var insert = base
    insert[kSecValueData as String] = try JSONEncoder().encode(original)
    #expect(SecItemAdd(insert as CFDictionary, nil) == errSecSuccess)
    let first = IXKeychainCodexCredentialStore(service: service)
    let second = IXKeychainCodexCredentialStore(service: service)
    #expect(try await first.replace(original, with: fresh))
    try await second.save(other)
    #expect(try await first.replace(fresh, with: original) == false)
    #expect(try await first.replace(fresh, with: nil) == false)
    #expect(try await second.load() == other)
    #expect(try await first.replace(other, with: nil))
    #expect(try await second.load() == nil)
    #expect(try await first.replace(nil, with: fresh))
    #expect(try await second.replace(nil, with: other) == false)
}
