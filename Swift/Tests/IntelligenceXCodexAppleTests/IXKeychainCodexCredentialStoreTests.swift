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
