import Foundation
import Testing
@testable import IntelligenceXCodex

@Test func externalCredentialWriteBetweenComparisonAndCommitWins() async throws {
    let original = IXCodexAuthBundle(accessToken: "original", refreshToken: "refresh")
    let stale = IXCodexAuthBundle(accessToken: "stale", refreshToken: "refresh")
    let replacement = IXCodexAuthBundle(accessToken: "replacement", refreshToken: "new-refresh")
    let store = SuspendedCredentialStore(bundle: original, suspendSaveOnce: true)
    let access = IXCodexCredentialAccess(store: store)
    let write = Task { try await access.save(stale,
        authorizedBy: IXCodexCredentialWriteAuthorization(), replacing: original) }
    await store.waitUntilSaveStarted()
    await store.save(replacement)
    await store.releaseSave()
    #expect(try await write.value == false)
    #expect(await store.currentBundle() == replacement)
}

@Test func revokedCredentialWriteCannotRollbackAnotherOwnersReplacement() async throws {
    let original = IXCodexAuthBundle(accessToken: "original", refreshToken: "refresh")
    let stale = IXCodexAuthBundle(accessToken: "stale", refreshToken: "refresh")
    let replacement = IXCodexAuthBundle(accessToken: "replacement", refreshToken: "new-refresh")
    let store = IXMemoryCodexCredentialStore(bundle: original)
    let access = IXCodexCredentialAccess(store: store)
    let authorization = IXCodexCredentialWriteAuthorization()
    #expect(try await access.save(stale, authorizedBy: authorization))
    await store.save(replacement)
    try await access.revoke([authorization.id])
    #expect(await store.load() == replacement)
}

@Test func transportFailureAfterAuthorizationRevocationCannotSendToFallback() async throws {
    let store = IXMemoryCodexCredentialStore(bundle: .init(accessToken: "old", refreshToken: "refresh", accountID: "account"))
    let failure = SuspendedHTTPFailure()
    let requests = AuthorizationRequestCounter()
    let http = IXClosureHTTPClient { request in
        await requests.record()
        return try await failure.send(request)
    }
    var configuration = IXCodexConfiguration()
    configuration.modelURLs = [URL(string: "https://example.test/first")!, URL(string: "https://example.test/fallback")!]
    let auth = IXCodexAuthSession(configuration: configuration, credentialStore: store, httpClient: http)
    let client = IXCodexClient(configuration: configuration, authSession: auth, httpClient: http)
    let models = Task { try await client.models() }
    await failure.waitUntilRequested()
    try await auth.signOut()
    await failure.release()
    do { _ = try await models.value; Issue.record("Revoked request must cancel") }
    catch is CancellationError {}
    #expect(await requests.count == 1)
}

private actor AuthorizationRequestCounter {
    var count = 0
    func record() { count += 1 }
}
