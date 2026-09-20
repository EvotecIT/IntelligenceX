import Foundation
import Testing
@testable import IntelligenceXCodex

@Test func modelCatalogReusesCredentialsRefreshedByConcurrentUsage() async throws {
    let credentials = IXMemoryCodexCredentialStore(bundle: .init(
        accessToken: "old", refreshToken: "refresh", expiresAt: .distantFuture, accountID: "account"))
    let transport = ModelAuthorizationTransport()
    var configuration = IXCodexConfiguration()
    configuration.modelURLs = [URL(string: "https://example.test/models")!]
    configuration.accountUsageURL = URL(string: "https://example.test/usage")!
    let http = IXClosureHTTPClient { request in try await transport.send(request) }
    let auth = IXCodexAuthSession(configuration: configuration, credentialStore: credentials, httpClient: http)
    let client = IXCodexClient(configuration: configuration, authSession: auth, httpClient: http)
    let catalog = Task { try await client.models() }
    await transport.waitUntilModelStarted()
    _ = try await client.accountUsage()
    await transport.release()
    #expect(try await catalog.value.map(\.id) == ["test-model"])
    #expect(await transport.modelTokens == ["Bearer old", "Bearer fresh"])
    #expect(await transport.refreshes == 1)
}

@Test func modelCatalogStillRejectsInvalidReplacementCredentials() async throws {
    let credentials = IXMemoryCodexCredentialStore(bundle: .init(
        accessToken: "old", refreshToken: "refresh", expiresAt: .distantFuture, accountID: "account"))
    let transport = ModelAuthorizationTransport(holdFirstModel: false, rejectFresh: true)
    var configuration = IXCodexConfiguration()
    configuration.modelURLs = [URL(string: "https://example.test/models")!]
    let http = IXClosureHTTPClient { request in try await transport.send(request) }
    let auth = IXCodexAuthSession(configuration: configuration, credentialStore: credentials, httpClient: http)
    let client = IXCodexClient(configuration: configuration, authSession: auth, httpClient: http)
    do {
        _ = try await client.models()
        Issue.record("Invalid current credentials must not become a successful catalog")
    } catch let error as IXCodexError { #expect(error.requiresReauthorization) }
    #expect(await transport.refreshes == 1)
    #expect(await transport.modelTokens.count == 2)
}

private actor ModelAuthorizationTransport {
    let holdFirstModel: Bool
    let rejectFresh: Bool
    let heldModelNumber: Int
    let heldPath: String
    var refreshes = 0
    var modelTokens: [String] = []
    private var pending: CheckedContinuation<Void, Never>?
    private var started: CheckedContinuation<Void, Never>?
    init(holdFirstModel: Bool = true, rejectFresh: Bool = false, heldModelNumber: Int = 1, heldPath: String = "/models") {
        self.holdFirstModel = holdFirstModel
        self.rejectFresh = rejectFresh
        self.heldModelNumber = heldModelNumber
        self.heldPath = heldPath
    }
    func send(_ request: URLRequest) async throws -> IXHTTPResponse {
        let token = request.value(forHTTPHeaderField: "Authorization") ?? ""
        if request.url?.path == heldPath {
            modelTokens.append(token)
            if holdFirstModel && modelTokens.count == heldModelNumber {
                await withCheckedContinuation { continuation in
                    pending = continuation; started?.resume(); started = nil
                }
            }
            if token == "Bearer old" || rejectFresh { return .init(statusCode: 401, body: Data()) }
            return .init(statusCode: 200, body: Data(#"{"models":[{"id":"test-model"}]}"#.utf8))
        }
        if request.url?.path == "/usage" {
            return token == "Bearer old" ? .init(statusCode: 401, body: Data())
                : .init(statusCode: 200, body: Data(#"{"plan_type":"plus"}"#.utf8))
        }
        refreshes += 1
        return .init(statusCode: 200, body: Data(#"{"access_token":"fresh","refresh_token":"next","expires_in":3600}"#.utf8))
    }
    func waitUntilModelStarted() async {
        guard pending == nil else { return }
        await withCheckedContinuation { started = $0 }
    }
    func release() { pending?.resume(); pending = nil }
}

@Test(arguments: [false, true])
func modelCatalogDoesNotRetryAcrossAccountReplacement(signOut: Bool) async throws {
    let credentials = IXMemoryCodexCredentialStore(bundle: .init(
        accessToken: "old", refreshToken: "refresh", expiresAt: .distantFuture, accountID: "account"))
    let transport = ModelAuthorizationTransport()
    var configuration = IXCodexConfiguration()
    configuration.modelURLs = [URL(string: "https://example.test/models")!]
    let http = IXClosureHTTPClient { request in try await transport.send(request) }
    let auth = IXCodexAuthSession(configuration: configuration, credentialStore: credentials, httpClient: http)
    let client = IXCodexClient(configuration: configuration, authSession: auth, httpClient: http)
    let catalog = Task { try await client.models() }
    await transport.waitUntilModelStarted()
    if signOut { try await auth.signOut() }
    else {
        await credentials.save(.init(accessToken: "other", refreshToken: "other-refresh",
            expiresAt: .distantFuture, accountID: "other-account"))
    }
    await transport.release()
    await #expect(throws: CancellationError.self) { try await catalog.value }
    #expect(await transport.refreshes == 0)
}

@Test(arguments: [false, true])
func modelCatalogRecoveryChecksCredentialsAtTheRefreshDecision(accountChanged: Bool) async throws {
    let rejected = IXCodexAuthBundle(accessToken: "old", refreshToken: "refresh",
        expiresAt: .distantFuture, accountID: "account")
    let credentials = SuspendedCredentialStore(bundle: rejected, suspendLoadOnce: true)
    let transport = ModelAuthorizationTransport(holdFirstModel: false)
    let http = IXClosureHTTPClient { request in try await transport.send(request) }
    let auth = IXCodexAuthSession(credentialStore: credentials, httpClient: http)
    let recovery = Task { try await auth.recoverRejectedBundle(rejected) }
    await credentials.waitUntilLoadStarted()
    await credentials.save(.init(accessToken: "fresh", refreshToken: "next",
        expiresAt: .distantFuture, accountID: accountChanged ? "other-account" : "account"))
    await credentials.releaseLoad()
    if accountChanged {
        await #expect(throws: CancellationError.self) { try await recovery.value }
    } else {
        #expect(try await recovery.value.bundle.accessToken == "fresh")
    }
    #expect(await transport.refreshes == 0)
}

@Test func modelCatalogDoesNotRevokeASecondReplacementDuringRetry() async throws {
    let credentials = IXMemoryCodexCredentialStore(bundle: .init(
        accessToken: "old", refreshToken: "refresh", expiresAt: .distantFuture, accountID: "account"))
    let transport = ModelAuthorizationTransport(rejectFresh: true, heldModelNumber: 2)
    var configuration = IXCodexConfiguration()
    configuration.modelURLs = [URL(string: "https://example.test/models")!]
    let http = IXClosureHTTPClient { request in try await transport.send(request) }
    let auth = IXCodexAuthSession(configuration: configuration, credentialStore: credentials, httpClient: http)
    let client = IXCodexClient(configuration: configuration, authSession: auth, httpClient: http)
    let catalog = Task { try await client.models() }
    await transport.waitUntilModelStarted()
    await credentials.save(.init(accessToken: "newest", refreshToken: "newest-refresh",
        expiresAt: .distantFuture, accountID: "account"))
    await transport.release()
    await #expect(throws: CancellationError.self) { try await catalog.value }
    #expect(await transport.modelTokens == ["Bearer old", "Bearer fresh"])
    #expect(await transport.refreshes == 1)
    #expect(try await auth.currentBundle()?.accessToken == "newest")
}

@Test(arguments: ["usage", "compact", "responses"], [false, true])
func rejectedRequestRecoveryPreservesAuthorizationAcrossSiblingRoutes(route: String, duringRetry: Bool) async throws {
    let credentials = IXMemoryCodexCredentialStore(bundle: .init(
        accessToken: "old", refreshToken: "refresh", expiresAt: .distantFuture, accountID: "account"))
    let transport = ModelAuthorizationTransport(rejectFresh: true,
        heldModelNumber: duringRetry ? 2 : 1, heldPath: "/" + route)
    var configuration = IXCodexConfiguration()
    configuration.accountUsageURL = URL(string: "https://example.test/usage")!
    configuration.compactionURL = URL(string: "https://example.test/compact")!
    configuration.responsesURL = URL(string: "https://example.test/responses")!
    let http = IXClosureHTTPClient { request in try await transport.send(request) }
    let auth = IXCodexAuthSession(configuration: configuration, credentialStore: credentials, httpClient: http)
    let client = IXCodexClient(configuration: configuration, authSession: auth, httpClient: http)
    let request = Task {
        switch route {
        case "usage": _ = try await client.accountUsage()
        case "compact": _ = try await client.compact(input: [], sessionID: "session", instructions: "", model: "test")
        default: _ = try await client.response(input: [], sessionID: "session", instructions: "", tools: [],
            model: "test", reasoningEffort: nil, webSearch: nil, imageGeneration: nil)
        }
    }
    await transport.waitUntilModelStarted()
    await credentials.save(.init(accessToken: "replacement", refreshToken: "replacement-refresh",
        expiresAt: .distantFuture, accountID: duringRetry ? "account" : "other-account"))
    await transport.release()
    await #expect(throws: CancellationError.self) { try await request.value }
    #expect(await transport.modelTokens == (duringRetry ? ["Bearer old", "Bearer fresh"] : ["Bearer old"]))
    #expect(await transport.refreshes == (duringRetry ? 1 : 0))
}

@Test(arguments: [false, true])
func recoveredAuthorizationIsRevokedBeforeReplay(signOut: Bool) async throws {
    let rejected = IXCodexAuthBundle(accessToken: "old", refreshToken: "refresh",
        expiresAt: .distantFuture, accountID: "account")
    let credentials = IXMemoryCodexCredentialStore(bundle: rejected)
    let transport = ModelAuthorizationTransport(holdFirstModel: false)
    let http = IXClosureHTTPClient { request in try await transport.send(request) }
    let configuration = IXCodexConfiguration()
    let auth = IXCodexAuthSession(configuration: configuration, credentialStore: credentials, httpClient: http)
    let recovered = try await auth.recoverRejectedBundle(rejected)
    if signOut { try await auth.signOut() }
    else {
        _ = try await auth.beginBrowserAuthorization(redirectURL:
            URL(string: "http://localhost:\(configuration.browserCallbackPorts[0])/auth/callback")!)
    }
    await #expect(throws: CancellationError.self) { try await auth.validateRecoveredAuthorization(recovered) }
}

@Test func modelCatalogRecoversEarlierAuthorizationFailureWhenFallbackFails() async throws {
    let credentials = IXMemoryCodexCredentialStore(bundle: .init(accessToken: "old", refreshToken: "refresh",
        expiresAt: .distantFuture, accountID: "account"))
    let transport = ModelAuthorizationTransport(holdFirstModel: false)
    let http = IXClosureHTTPClient { request in
        if request.url?.path == "/fallback" { return .init(statusCode: 503, body: Data()) }
        return try await transport.send(request)
    }
    var configuration = IXCodexConfiguration()
    configuration.modelURLs = [URL(string: "https://example.test/models")!, URL(string: "https://example.test/fallback")!]
    let auth = IXCodexAuthSession(configuration: configuration, credentialStore: credentials, httpClient: http)
    let client = IXCodexClient(configuration: configuration, authSession: auth, httpClient: http)
    #expect(try await client.models().map(\.id) == ["test-model"])
    #expect(await transport.refreshes == 1)
}

@Test func rejectedRecoveryCannotPersistAnAccountChangingRefresh() async throws {
    let rejected = IXCodexAuthBundle(accessToken: "old", refreshToken: "refresh",
        expiresAt: .distantFuture, accountID: "account")
    let credentials = IXMemoryCodexCredentialStore(bundle: rejected)
    let payload = Data(#"{"https://api.openai.com/auth":{"chatgpt_account_id":"other-account"}}"#.utf8)
        .base64EncodedString().replacingOccurrences(of: "=", with: "")
    let token = "e30.\(payload).signature"
    let http = IXClosureHTTPClient { _ in
        .init(statusCode: 200, body: try JSONSerialization.data(withJSONObject: [
            "access_token": token, "refresh_token": "next", "expires_in": 3600]))
    }
    let auth = IXCodexAuthSession(credentialStore: credentials, httpClient: http)
    await #expect(throws: CancellationError.self) { try await auth.recoverRejectedBundle(rejected) }
    #expect(try await auth.currentBundle()?.accessToken == "old")
}

@Test func rejectedRecoveryRefreshesAnExpiredReplacementInsteadOfSendingIt() async throws {
    let rejected = IXCodexAuthBundle(accessToken: "old", refreshToken: "refresh",
        expiresAt: .distantFuture, accountID: "account")
    let credentials = IXMemoryCodexCredentialStore(bundle: .init(accessToken: "expired-replacement",
        refreshToken: "replacement-refresh", expiresAt: .distantPast, accountID: "account"))
    let transport = ModelAuthorizationTransport(holdFirstModel: false)
    let http = IXClosureHTTPClient { request in try await transport.send(request) }
    let auth = IXCodexAuthSession(credentialStore: credentials, httpClient: http)
    let recovered = try await auth.recoverRejectedBundle(rejected)
    #expect(recovered.bundle.accessToken == "fresh")
    #expect(await transport.refreshes == 1)
}
