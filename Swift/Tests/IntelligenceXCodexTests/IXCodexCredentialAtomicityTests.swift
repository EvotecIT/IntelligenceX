import Foundation
import Testing
@testable import IntelligenceXCodex

@Test(arguments: [false, true]) func externalCredentialWriteBetweenComparisonAndCommitWins(identical: Bool) async throws {
    let original = IXCodexAuthBundle(accessToken: "original", refreshToken: "refresh")
    let stale = IXCodexAuthBundle(accessToken: "stale", refreshToken: "refresh")
    let replacement = identical ? original : IXCodexAuthBundle(accessToken: "replacement", refreshToken: "new-refresh")
    let store = SuspendedCredentialStore(bundle: original, suspendSaveOnce: true)
    let access = IXCodexCredentialAccess(store: store)
    let observed = try await store.snapshot()
    let write = Task { try await access.save(stale,
        authorizedBy: IXCodexCredentialWriteAuthorization(), replacing: observed) }
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

@Test func revokedLocalLayersPreserveAnInterveningExternalWrite() async throws {
    let original = IXCodexAuthBundle(accessToken: "original", refreshToken: "r")
    let first = IXCodexAuthBundle(accessToken: "first", refreshToken: "r")
    let external = IXCodexAuthBundle(accessToken: "external", refreshToken: "r")
    let second = IXCodexAuthBundle(accessToken: "second", refreshToken: "r")
    let store = IXMemoryCodexCredentialStore(bundle: original)
    let access = IXCodexCredentialAccess(store: store)
    let a = IXCodexCredentialWriteAuthorization()
    let b = IXCodexCredentialWriteAuthorization()
    #expect(try await access.save(first, authorizedBy: a))
    await store.save(external)
    #expect(try await access.save(second, authorizedBy: b))
    try await access.revoke([a.id, b.id])
    #expect(await store.load() == external)
}

@Test func successfulSignInRecoversAfterTransientCredentialStorageFailure() async throws {
    let original = IXCodexAuthBundle(accessToken: "quarantined", refreshToken: "r")
    let fresh = IXCodexAuthBundle(accessToken: "fresh", refreshToken: "r")
    let store = SuspendedCredentialStore(bundle: original, failLoadOnce: true)
    let access = IXCodexCredentialAccess(store: store)
    do { _ = try await access.save(fresh, authorizedBy: IXCodexCredentialWriteAuthorization())
        Issue.record("Initial storage read must fail")
    } catch {}
    let authorization = IXCodexCredentialWriteAuthorization()
    #expect(try await access.save(fresh, authorizedBy: authorization))
    #expect(try await access.load() == fresh)
    try await access.revoke([authorization.id])
    #expect(try await access.load() == nil)
}

@Test func identicalExternalSaveCannotBeRolledBackByAnEarlierOwner() async throws {
    let original = IXCodexAuthBundle(accessToken: "old", refreshToken: "r")
    let fresh = IXCodexAuthBundle(accessToken: "fresh", refreshToken: "r")
    let store = IXMemoryCodexCredentialStore(bundle: original)
    let access = IXCodexCredentialAccess(store: store)
    let authorization = IXCodexCredentialWriteAuthorization()
    #expect(try await access.save(fresh, authorizedBy: authorization))
    await store.save(fresh)
    try await access.revoke([authorization.id])
    #expect(await store.load() == fresh)
}

@Test func signInRepairsPersistentlyUnreadableCredentialsWithoutSignOut() async throws {
    let store = SuspendedCredentialStore(bundle: nil, unreadable: true)
    let access = IXCodexCredentialAccess(store: store)
    await #expect(throws: IXCodexError.self) { try await access.load() }
    let fresh = IXCodexAuthBundle(accessToken: "fresh", refreshToken: "r")
    #expect(try await access.save(fresh, authorizedBy: IXCodexCredentialWriteAuthorization()))
    #expect(try await access.load() == fresh)
}

@Test func replacingCredentialsStartsItsOwnRefreshInsteadOfJoiningTheOldSource() async throws {
    let old = IXCodexAuthBundle(accessToken: "a", refreshToken: "refresh-a", accountID: "same")
    let replacement = IXCodexAuthBundle(accessToken: "b", refreshToken: "refresh-b", accountID: "same")
    let store = IXMemoryCodexCredentialStore(bundle: old)
    let suspended = SuspendedHTTPResponse(response: .json(200, ["access_token": "fresh-a", "refresh_token": "r-a"]))
    let http = IXClosureHTTPClient { request in
        if String(data: request.httpBody ?? Data(), encoding: .utf8)?.contains("refresh-a") == true {
            return try await suspended.send(request)
        }
        return .json(200, ["access_token": "fresh-b", "refresh_token": "r-b"])
    }
    let auth = IXCodexAuthSession(credentialStore: store, httpClient: http)
    let first = Task { try await auth.validBundle(forceRefresh: true) }
    await suspended.waitUntilRequested()
    await store.save(replacement)
    let second = try await auth.validBundle(forceRefresh: true)
    #expect(second.accessToken == "fresh-b")
    await suspended.release()
    await #expect(throws: CancellationError.self) { try await first.value }
    #expect(await store.load()?.accessToken == "fresh-b")
}

@Test func canceledWriteRestorationKeepsEarlierRevocationPossible() async throws {
    let original = IXCodexAuthBundle(accessToken: "original", refreshToken: "r")
    let first = IXCodexAuthBundle(accessToken: "first", refreshToken: "r")
    let second = IXCodexAuthBundle(accessToken: "second", refreshToken: "r")
    let store = SuspendedCredentialStore(bundle: original)
    let access = IXCodexCredentialAccess(store: store)
    let a = IXCodexCredentialWriteAuthorization()
    let b = IXCodexCredentialWriteAuthorization()
    #expect(try await access.save(first, authorizedBy: a))
    await store.suspendNextSave()
    let write = Task { try await access.save(second, authorizedBy: b) }
    await store.waitUntilSaveStarted()
    b.invalidate()
    await store.releaseSave()
    #expect(try await write.value == false)
    #expect(await store.currentBundle() == first)
    try await access.revoke([a.id])
    #expect(await store.currentBundle() == original)
}

@Test func refreshCannotOverwriteIdenticalCredentialsResavedByAnotherOwner() async throws {
    let original = IXCodexAuthBundle(accessToken: "old", refreshToken: "r", accountID: "same")
    let store = IXMemoryCodexCredentialStore(bundle: original)
    let response = SuspendedHTTPResponse(response: .json(200, ["access_token": "stale", "refresh_token": "stale-r"]))
    let auth = IXCodexAuthSession(credentialStore: store, httpClient: IXClosureHTTPClient { try await response.send($0) })
    let refresh = Task { try await auth.validBundle(forceRefresh: true) }
    await response.waitUntilRequested()
    await store.save(original)
    await response.release()
    await #expect(throws: CancellationError.self) { try await refresh.value }
    #expect(await store.load() == original)
}
