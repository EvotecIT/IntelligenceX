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

public protocol IXCodexCredentialStoring: Sendable {
    func load() async throws -> IXCodexAuthBundle?
    func save(_ bundle: IXCodexAuthBundle) async throws
    func delete() async throws
}

public actor IXMemoryCodexCredentialStore: IXCodexCredentialStoring {
    private var bundle: IXCodexAuthBundle?

    public init(bundle: IXCodexAuthBundle? = nil) {
        self.bundle = bundle
    }

    public func load() -> IXCodexAuthBundle? { bundle }
    public func save(_ bundle: IXCodexAuthBundle) { self.bundle = bundle }
    public func delete() { bundle = nil }
}

public struct IXCodexDeviceCode: Equatable, Sendable {
    public let deviceAuthorizationID: String
    public let userCode: String
    public let verificationURL: URL
    public let interval: Duration
    public let expiresAt: Date

    public init(
        deviceAuthorizationID: String,
        userCode: String,
        verificationURL: URL,
        interval: Duration,
        expiresAt: Date
    ) {
        self.deviceAuthorizationID = deviceAuthorizationID
        self.userCode = userCode
        self.verificationURL = verificationURL
        self.interval = interval
        self.expiresAt = expiresAt
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
