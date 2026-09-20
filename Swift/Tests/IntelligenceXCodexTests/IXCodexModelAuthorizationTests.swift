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
    var refreshes = 0
    var modelTokens: [String] = []
    private var pending: CheckedContinuation<Void, Never>?
    private var started: CheckedContinuation<Void, Never>?
    init(holdFirstModel: Bool = true, rejectFresh: Bool = false) {
        self.holdFirstModel = holdFirstModel
        self.rejectFresh = rejectFresh
    }
    func send(_ request: URLRequest) async throws -> IXHTTPResponse {
        let token = request.value(forHTTPHeaderField: "Authorization") ?? ""
        if request.url?.path == "/models" {
            modelTokens.append(token)
            if holdFirstModel && modelTokens.count == 1 {
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
