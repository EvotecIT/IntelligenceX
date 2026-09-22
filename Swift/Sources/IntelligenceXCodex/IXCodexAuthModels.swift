import Foundation

public struct IXCodexAuthBundle: Codable, Equatable, Sendable {
    public var accessToken: String
    public var refreshToken: String
    public var expiresAt: Date?
    public var accountID: String?
    public var idToken: String?
    public var tokenType: String?
    public var scope: String?

    public init(
        accessToken: String,
        refreshToken: String,
        expiresAt: Date? = nil,
        accountID: String? = nil,
        idToken: String? = nil,
        tokenType: String? = nil,
        scope: String? = nil
    ) {
        self.accessToken = accessToken
        self.refreshToken = refreshToken
        self.expiresAt = expiresAt
        self.accountID = accountID
        self.idToken = idToken
        self.tokenType = tokenType
        self.scope = scope
    }

    public func needsRefresh(at date: Date = Date(), tolerance: TimeInterval = 120) -> Bool {
        guard let expiresAt else { return false }
        return expiresAt.timeIntervalSince(date) <= tolerance
    }
}

/// An opaque storage revision and its decoded credentials. An unreadable value
/// still carries a revision so a fresh sign-in can atomically repair it.
public struct IXCodexCredentialSnapshot: Equatable, Sendable {
    public let bundle: IXCodexAuthBundle?
    public let revision: Data?
    public let isUnreadable: Bool

    public init(bundle: IXCodexAuthBundle?, revision: Data?, isUnreadable: Bool = false) {
        self.bundle = bundle
        self.revision = revision
        self.isUnreadable = isUnreadable
    }

    public func validatedBundle() throws -> IXCodexAuthBundle? {
        guard !isUnreadable else {
            throw IXCodexError.invalidResponse("Stored ChatGPT credentials are unreadable. Sign in again.")
        }
        return bundle
    }
}

public protocol IXCodexCredentialStoring: Sendable {
    func load() async throws -> IXCodexAuthBundle?
    func save(_ bundle: IXCodexAuthBundle) async throws
    func delete() async throws
    func snapshot() async throws -> IXCodexCredentialSnapshot
    /// Atomically replaces the exact observed revision, returning the newly
    /// committed snapshot, or nil if another write superseded it. Every write,
    /// including identical values and deletions, must create a new revision.
    func replace(_ expected: IXCodexCredentialSnapshot, with replacement: IXCodexAuthBundle?) async throws -> IXCodexCredentialSnapshot?
}

public actor IXMemoryCodexCredentialStore: IXCodexCredentialStoring {
    private var bundle: IXCodexAuthBundle?
    private var revision = Data(UUID().uuidString.utf8)

    public init(bundle: IXCodexAuthBundle? = nil) { self.bundle = bundle }
    public func load() -> IXCodexAuthBundle? { bundle }
    public func snapshot() -> IXCodexCredentialSnapshot { .init(bundle: bundle, revision: revision) }
    public func save(_ bundle: IXCodexAuthBundle) { self.bundle = bundle; revision = Data(UUID().uuidString.utf8) }
    public func delete() { bundle = nil; revision = Data(UUID().uuidString.utf8) }
    public func replace(_ expected: IXCodexCredentialSnapshot, with replacement: IXCodexAuthBundle?) -> IXCodexCredentialSnapshot? {
        guard revision == expected.revision else { return nil }
        bundle = replacement
        revision = Data(UUID().uuidString.utf8)
        return snapshot()
    }
}

public struct IXCodexDeviceCode: Equatable, Sendable {
    public let deviceAuthorizationID: String
    public let userCode: String
    public let verificationURL: URL
    public let interval: Duration
    public let expiresAt: Date
    let expectedAuthGeneration: UInt64

    public init(
        deviceAuthorizationID: String,
        userCode: String,
        verificationURL: URL,
        interval: Duration,
        expiresAt: Date,
        expectedAuthGeneration: UInt64 = 0
    ) {
        self.deviceAuthorizationID = deviceAuthorizationID
        self.userCode = userCode
        self.verificationURL = verificationURL
        self.interval = interval
        self.expiresAt = expiresAt
        self.expectedAuthGeneration = expectedAuthGeneration
    }
}

public struct IXCodexAccount: Equatable, Sendable {
    public let id: String
    public let email: String?
    public let plan: String?

    public init(id: String, email: String? = nil, plan: String? = nil) {
        self.id = id
        self.email = email
        self.plan = plan
    }
}

public struct IXCodexAuthorizationSnapshot: Equatable, Sendable {
    public let account: IXCodexAccount?
    public let accessTokenExpiresAt: Date?
    public let accessTokenNeedsRefresh: Bool

    public init(
        account: IXCodexAccount?,
        accessTokenExpiresAt: Date?,
        accessTokenNeedsRefresh: Bool
    ) {
        self.account = account
        self.accessTokenExpiresAt = accessTokenExpiresAt
        self.accessTokenNeedsRefresh = accessTokenNeedsRefresh
    }
}
