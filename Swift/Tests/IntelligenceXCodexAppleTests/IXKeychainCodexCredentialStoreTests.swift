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
    let originalSnapshot = try await first.snapshot()
    let freshSnapshot = try #require(try await first.replace(originalSnapshot, with: fresh))
    try await second.save(other)
    #expect(try await first.replace(freshSnapshot, with: original) == nil)
    #expect(try await first.replace(freshSnapshot, with: nil) == nil)
    #expect(try await second.load() == other)
    let otherSnapshot = try await second.snapshot()
    let deleted = try #require(try await first.replace(otherSnapshot, with: nil))
    #expect(try await second.load() == nil)
    #expect(try await first.replace(deleted, with: fresh) != nil)
    #expect(try await second.replace(deleted, with: other) == nil)
    let beforeIdenticalSave = try await first.snapshot()
    try await second.save(fresh)
    #expect(try await first.replace(beforeIdenticalSave, with: original) == nil)
    #expect(try await first.replace(beforeIdenticalSave, with: nil) == nil)
    // Corrupt bytes remain replaceable by their observed revision.
    #expect(SecItemUpdate(base as CFDictionary, [kSecValueData as String: Data("invalid".utf8)] as CFDictionary) == errSecSuccess)
    let corrupt = try await first.snapshot()
    #expect(corrupt.isUnreadable)
    #expect(try await second.replace(corrupt, with: original) != nil)
    #expect(try await first.load() == original)
}
