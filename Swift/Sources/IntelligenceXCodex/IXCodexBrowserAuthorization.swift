import CryptoKit
import Foundation

/// A short-lived, PKCE-protected ChatGPT browser authorization request.
public struct IXCodexBrowserAuthorization: Equatable, Sendable {
    public let authorizationURL: URL
    public let redirectURL: URL
    public let expiresAt: Date

    let state: String
    let codeVerifier: String
    let expectedAuthGeneration: UInt64

    init(
        authorizationURL: URL,
        redirectURL: URL,
        expiresAt: Date,
        state: String,
        codeVerifier: String,
        expectedAuthGeneration: UInt64
    ) {
        self.authorizationURL = authorizationURL
        self.redirectURL = redirectURL
        self.expiresAt = expiresAt
        self.state = state
        self.codeVerifier = codeVerifier
        self.expectedAuthGeneration = expectedAuthGeneration
    }
}

enum IXCodexPKCE {
    static func verifier(randomBytes: [UInt8]) -> String {
        base64URL(Data(randomBytes))
    }

    static func challenge(for verifier: String) -> String {
        base64URL(Data(SHA256.hash(data: Data(verifier.utf8))))
    }

    static func state(randomBytes: [UInt8]) -> String {
        base64URL(Data(randomBytes))
    }

    private static func base64URL(_ data: Data) -> String {
        data.base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }
}
