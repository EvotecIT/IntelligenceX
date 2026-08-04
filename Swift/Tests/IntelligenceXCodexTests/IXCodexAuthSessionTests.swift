import Foundation
@testable import IntelligenceXCodex
import XCTest

final class IXCodexAuthSessionTests: XCTestCase {
    func testDeviceAuthorizationCompletesAndPersistsRotatableBundle() async throws {
        let state = ResponseQueue(responses: [
            .json(200, [
                "device_auth_id": "device-1",
                "user_code": "ABCD-1234",
                "interval": "0",
            ]),
            .json(200, [
                "authorization_code": "authorization-code",
                "code_challenge": "challenge",
                "code_verifier": "verifier",
            ]),
            .json(200, [
                "access_token": "access-token",
                "refresh_token": "refresh-token",
                "expires_in": 3_600,
                "token_type": "Bearer",
            ]),
        ])
        let store = IXMemoryCodexCredentialStore()
        let session = IXCodexAuthSession(
            credentialStore: store,
            httpClient: IXClosureHTTPClient { request in try await state.next(request) }
        )

        let code = try await session.beginDeviceAuthorization()
        XCTAssertEqual(code.userCode, "ABCD-1234")
        let bundle = try await session.completeDeviceAuthorization(code)

        XCTAssertEqual(bundle.accessToken, "access-token")
        XCTAssertEqual(bundle.refreshToken, "refresh-token")
        let persistedBundle = await store.load()
        XCTAssertEqual(persistedBundle, bundle)
        let requests = await state.requests
        XCTAssertEqual(requests.map(\.url?.path), [
            "/api/accounts/deviceauth/usercode",
            "/api/accounts/deviceauth/token",
            "/oauth/token",
        ])
        XCTAssertTrue(String(data: try XCTUnwrap(requests.last?.httpBody), encoding: .utf8)?.contains("code_verifier=verifier") == true)
    }

    func testRefreshUsesLatestRotatedRefreshToken() async throws {
        let expired = IXCodexAuthBundle(
            accessToken: "old-access",
            refreshToken: "old-refresh",
            expiresAt: .distantPast,
            accountID: "account-1"
        )
        let store = IXMemoryCodexCredentialStore(bundle: expired)
        let state = ResponseQueue(responses: [
            .json(200, [
                "access_token": "new-access",
                "refresh_token": "new-refresh",
                "expires_in": 3_600,
            ]),
        ])
        let session = IXCodexAuthSession(
            credentialStore: store,
            httpClient: IXClosureHTTPClient { request in try await state.next(request) }
        )

        let refreshed = try await session.validBundle()

        XCTAssertEqual(refreshed.accessToken, "new-access")
        XCTAssertEqual(refreshed.refreshToken, "new-refresh")
        let persistedRefreshToken = await store.load()?.refreshToken
        XCTAssertEqual(persistedRefreshToken, "new-refresh")
    }

    func testSignOutPreventsSuspendedRefreshFromRestoringCredentials() async throws {
        let store = IXMemoryCodexCredentialStore(bundle: .init(
            accessToken: "expired",
            refreshToken: "old-refresh",
            expiresAt: .distantPast,
            accountID: "account"
        ))
        let gate = SuspendedHTTPResponse(response: .json(200, [
            "access_token": "new-access",
            "refresh_token": "new-refresh",
            "expires_in": 3600,
        ]))
        let session = IXCodexAuthSession(
            credentialStore: store,
            httpClient: IXClosureHTTPClient { request in try await gate.send(request) }
        )

        let refresh = Task { try await session.validBundle() }
        await gate.waitUntilRequested()
        try await session.signOut()
        await gate.release()

        do {
            _ = try await refresh.value
            XCTFail("Refresh should be invalidated by sign-out")
        } catch is CancellationError {
        }
        let persisted = await store.load()
        XCTAssertNil(persisted)
    }

    func testNewAuthorizationInvalidatesBundleLoadedBySuspendedStore() async throws {
        let expired = IXCodexAuthBundle(
            accessToken: "expired",
            refreshToken: "old-refresh",
            expiresAt: .distantPast,
            accountID: "account"
        )
        let store = SuspendedCredentialStore(bundle: expired, suspendLoadOnce: true)
        let state = ResponseQueue(responses: [])
        let session = IXCodexAuthSession(
            credentialStore: store,
            httpClient: IXClosureHTTPClient { request in try await state.next(request) }
        )

        let refresh = Task { try await session.validBundle() }
        await store.waitUntilLoadStarted()
        let callbackPorts = await session.browserCallbackPorts
        let port = try XCTUnwrap(callbackPorts.first)
        _ = try await session.beginBrowserAuthorization(
            redirectURL: try XCTUnwrap(URL(string: "http://localhost:\(port)/auth/callback"))
        )
        await store.releaseLoad()

        do {
            _ = try await refresh.value
            XCTFail("A bundle loaded for the previous authorization generation must be ignored")
        } catch is CancellationError {
        }
        let requests = await state.requests
        XCTAssertTrue(requests.isEmpty)
    }

    func testSignOutIsSerializedAfterSuspendedCredentialSave() async throws {
        let store = SuspendedCredentialStore(bundle: .init(
            accessToken: "expired",
            refreshToken: "old-refresh",
            expiresAt: .distantPast,
            accountID: "account"
        ), suspendSaveOnce: true)
        let session = IXCodexAuthSession(
            credentialStore: store,
            httpClient: IXClosureHTTPClient { _ in
                .json(200, [
                    "access_token": "new-access",
                    "refresh_token": "new-refresh",
                    "expires_in": 3600,
                ])
            }
        )

        let refresh = Task { try await session.validBundle() }
        await store.waitUntilSaveStarted()
        let signOut = Task { try await session.signOut() }
        await store.releaseSave()
        try await signOut.value

        do {
            _ = try await refresh.value
            XCTFail("The refresh must be invalidated by sign-out")
        } catch is CancellationError {
        }
        let persisted = await store.currentBundle()
        XCTAssertNil(persisted)
    }

    func testCancellationDuringCredentialSaveRestoresThePreviousBundle() async throws {
        let previous = IXCodexAuthBundle(
            accessToken: "previous-access",
            refreshToken: "previous-refresh",
            expiresAt: .distantFuture,
            accountID: "previous-account"
        )
        let store = SuspendedCredentialStore(
            bundle: previous,
            suspendSaveOnce: true
        )
        let session = IXCodexAuthSession(
            credentialStore: store,
            httpClient: IXClosureHTTPClient { _ in
                .json(200, [
                    "access_token": "canceled-access",
                    "refresh_token": "canceled-refresh",
                    "expires_in": 3_600,
                ])
            }
        )
        let callbackPorts = await session.browserCallbackPorts
        let callbackPort = try XCTUnwrap(callbackPorts.first)
        let redirectURL = try XCTUnwrap(URL(
            string: "http://localhost:\(callbackPort)/auth/callback"
        ))
        let authorization = try await session.beginBrowserAuthorization(
            redirectURL: redirectURL
        )
        let callbackURL = try XCTUnwrap(URL(
            string: "\(redirectURL.absoluteString)?code=code&state=\(authorization.state)"
        ))

        let completion = Task {
            try await session.completeBrowserAuthorization(
                authorization,
                callbackURL: callbackURL
            )
        }
        await store.waitUntilSaveStarted()
        completion.cancel()
        await store.releaseSave()

        do {
            _ = try await completion.value
            XCTFail("A canceled authorization must not return credentials")
        } catch is CancellationError {
        }
        let persisted = await store.currentBundle()
        XCTAssertEqual(persisted, previous)
    }

    func testNewAuthorizationDuringCredentialSaveRestoresThePreviousBundle() async throws {
        let previous = IXCodexAuthBundle(
            accessToken: "previous-access",
            refreshToken: "previous-refresh",
            expiresAt: .distantFuture,
            accountID: "previous-account"
        )
        let store = SuspendedCredentialStore(
            bundle: previous,
            suspendSaveOnce: true
        )
        let session = IXCodexAuthSession(
            credentialStore: store,
            httpClient: IXClosureHTTPClient { _ in
                .json(200, [
                    "access_token": "superseded-access",
                    "refresh_token": "superseded-refresh",
                    "expires_in": 3_600,
                ])
            }
        )
        let callbackPorts = await session.browserCallbackPorts
        let callbackPort = try XCTUnwrap(callbackPorts.first)
        let redirectURL = try XCTUnwrap(URL(
            string: "http://localhost:\(callbackPort)/auth/callback"
        ))
        let authorization = try await session.beginBrowserAuthorization(
            redirectURL: redirectURL
        )
        let callbackURL = try XCTUnwrap(URL(
            string: "\(redirectURL.absoluteString)?code=code&state=\(authorization.state)"
        ))
        let completion = Task {
            try await session.completeBrowserAuthorization(
                authorization,
                callbackURL: callbackURL
            )
        }
        await store.waitUntilSaveStarted()

        let replacement = Task {
            try await session.beginBrowserAuthorization(
                redirectURL: redirectURL
            )
        }
        for _ in 0..<10 { await Task.yield() }
        await store.releaseSave()
        _ = try await replacement.value

        do {
            _ = try await completion.value
            XCTFail("A superseded authorization must not return credentials")
        } catch is CancellationError {
        }
        let persisted = await store.currentBundle()
        XCTAssertEqual(persisted, previous)
    }

    func testFailedCanceledWriteCleanupKeepsCredentialReadsFailClosed()
        async throws {
        let store = SuspendedCredentialStore(
            bundle: nil,
            suspendSaveOnce: true,
            failDeleteOnce: true
        )
        let session = IXCodexAuthSession(
            credentialStore: store,
            httpClient: IXClosureHTTPClient { _ in
                .json(200, [
                    "access_token": "canceled-access",
                    "refresh_token": "canceled-refresh",
                    "expires_in": 3_600,
                ])
            }
        )
        let callbackPorts = await session.browserCallbackPorts
        let callbackPort = try XCTUnwrap(callbackPorts.first)
        let redirectURL = try XCTUnwrap(URL(
            string: "http://localhost:\(callbackPort)/auth/callback"
        ))
        let authorization = try await session.beginBrowserAuthorization(
            redirectURL: redirectURL
        )
        let callbackURL = try XCTUnwrap(URL(
            string: "\(redirectURL.absoluteString)?code=code&state=\(authorization.state)"
        ))
        let completion = Task {
            try await session.completeBrowserAuthorization(
                authorization,
                callbackURL: callbackURL
            )
        }
        await store.waitUntilSaveStarted()
        completion.cancel()
        await store.releaseSave()

        do {
            _ = try await completion.value
            XCTFail("Canceled authorization must not return credentials")
        } catch is CancellationError {
        }
        do {
            _ = try await session.currentBundle()
            XCTFail("A failed rollback must keep credential reads fail closed")
        } catch IXCodexError.invalidResponse(let message) {
            XCTAssertTrue(message.contains("could not be restored"))
        }

        try await session.signOut()
        let signedOutBundle = try await session.currentBundle()
        XCTAssertNil(signedOutBundle)
    }

    func testRevokingOverlappingWritesSkipsEveryCanceledLayer()
        async throws {
        let original = IXCodexAuthBundle(
            accessToken: "original-access",
            refreshToken: "original-refresh"
        )
        let first = IXCodexAuthBundle(
            accessToken: "first-access",
            refreshToken: "first-refresh"
        )
        let second = IXCodexAuthBundle(
            accessToken: "second-access",
            refreshToken: "second-refresh"
        )
        let store = IXMemoryCodexCredentialStore(bundle: original)
        let access = IXCodexCredentialAccess(store: store)
        let firstAuthorization = IXCodexCredentialWriteAuthorization()
        let secondAuthorization = IXCodexCredentialWriteAuthorization()

        let firstCommitted = try await access.save(
            first,
            authorizedBy: firstAuthorization
        )
        XCTAssertTrue(firstCommitted)
        let secondCommitted = try await access.save(
            second,
            authorizedBy: secondAuthorization
        )
        XCTAssertTrue(secondCommitted)

        try await access.revoke([firstAuthorization.id])
        let afterFirstRevocation = await store.load()
        XCTAssertEqual(afterFirstRevocation, second)
        try await access.revoke([secondAuthorization.id])
        let afterSecondRevocation = await store.load()
        XCTAssertEqual(afterSecondRevocation, original)
    }

    func testFinalizedCredentialWriteCannotBeRevokedLater() async throws {
        let original = IXCodexAuthBundle(
            accessToken: "original-access",
            refreshToken: "original-refresh"
        )
        let settled = IXCodexAuthBundle(
            accessToken: "settled-access",
            refreshToken: "settled-refresh"
        )
        let store = IXMemoryCodexCredentialStore(bundle: original)
        let access = IXCodexCredentialAccess(store: store)
        let authorization = IXCodexCredentialWriteAuthorization()

        let committed = try await access.save(
            settled,
            authorizedBy: authorization
        )
        XCTAssertTrue(committed)
        await access.finalize(authorization.id)
        try await access.revoke([authorization.id])

        let persisted = await store.load()
        XCTAssertEqual(persisted, settled)
    }

    func testCanceledNilCredentialLoadPrefersCancellation()
        async throws {
        let store = SuspendedCredentialStore(
            bundle: nil,
            suspendLoadOnce: true
        )
        let session = IXCodexAuthSession(credentialStore: store)
        let load = Task { try await session.validBundle() }
        await store.waitUntilLoadStarted()

        load.cancel()
        await store.releaseLoad()

        do {
            _ = try await load.value
            XCTFail("Canceled load must not report authenticationRequired")
        } catch is CancellationError {
        }
    }

    func testCanceledFailingCredentialLoadPrefersCancellation()
        async throws {
        let store = SuspendedCredentialStore(
            bundle: nil,
            suspendLoadOnce: true,
            failLoadOnce: true
        )
        let session = IXCodexAuthSession(credentialStore: store)
        let load = Task { try await session.validBundle() }
        await store.waitUntilLoadStarted()

        load.cancel()
        await store.releaseLoad()

        do {
            _ = try await load.value
            XCTFail("Canceled load must supersede a late store failure")
        } catch is CancellationError {
        }
    }

    func testConcurrentRefreshUsesOneNetworkRequest() async throws {
        let store = IXMemoryCodexCredentialStore(bundle: .init(
            accessToken: "expired",
            refreshToken: "old-refresh",
            expiresAt: .distantPast,
            accountID: "account"
        ))
        let gate = SuspendedHTTPResponse(response: .json(200, [
            "access_token": "new-access",
            "refresh_token": "new-refresh",
            "expires_in": 3600,
        ]))
        let session = IXCodexAuthSession(
            credentialStore: store,
            httpClient: IXClosureHTTPClient { request in try await gate.send(request) }
        )

        async let first = session.validBundle()
        async let second = session.validBundle()
        await gate.waitUntilRequested()
        await gate.release()
        let bundles = try await [first, second]
        let requestCount = await gate.requestCount

        XCTAssertEqual(bundles.map(\.accessToken), ["new-access", "new-access"])
        XCTAssertEqual(requestCount, 1)
    }

    func testCanceledSharedRefreshWaiterCannotReturnTheSharedSuccess()
        async throws {
        let store = IXMemoryCodexCredentialStore(bundle: .init(
            accessToken: "expired",
            refreshToken: "old-refresh",
            expiresAt: .distantPast,
            accountID: "account"
        ))
        let gate = SuspendedHTTPResponse(response: .json(200, [
            "access_token": "new-access",
            "refresh_token": "new-refresh",
            "expires_in": 3_600,
        ]))
        let session = IXCodexAuthSession(
            credentialStore: store,
            httpClient: IXClosureHTTPClient { request in
                try await gate.send(request)
            }
        )
        let owner = Task { try await session.validBundle() }
        await gate.waitUntilRequested()
        let waiter = Task { try await session.validBundle() }
        for _ in 0..<20 { await Task.yield() }

        waiter.cancel()
        await gate.release()

        let ownerBundle = try await owner.value
        XCTAssertEqual(ownerBundle.accessToken, "new-access")
        do {
            _ = try await waiter.value
            XCTFail("A canceled waiter must not return a shared refresh")
        } catch is CancellationError {
        }
        let requestCount = await gate.requestCount
        XCTAssertEqual(requestCount, 1)
    }

    func testCanceledTokenExchangePrefersCancellationOverLateHTTPFailure()
        async throws {
        let gate = SuspendedHTTPFailure()
        let session = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(),
            httpClient: IXClosureHTTPClient { request in
                try await gate.send(request)
            }
        )
        let callbackPorts = await session.browserCallbackPorts
        let callbackPort = try XCTUnwrap(callbackPorts.first)
        let redirectURL = try XCTUnwrap(URL(
            string: "http://localhost:\(callbackPort)/auth/callback"
        ))
        let authorization = try await session.beginBrowserAuthorization(
            redirectURL: redirectURL
        )
        let callbackURL = try XCTUnwrap(URL(
            string: "\(redirectURL.absoluteString)?code=code&state=\(authorization.state)"
        ))
        let completion = Task {
            try await session.completeBrowserAuthorization(
                authorization,
                callbackURL: callbackURL
            )
        }
        await gate.waitUntilRequested()

        completion.cancel()
        await gate.release()

        do {
            _ = try await completion.value
            XCTFail("Cancellation must supersede a late token HTTP failure")
        } catch is CancellationError {
        }
    }

    func testCanceledDevicePollPrefersCancellationOverLateHTTPFailure()
        async throws {
        let gate = DeviceAuthorizationFailureGate()
        let session = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(),
            httpClient: IXClosureHTTPClient { request in
                try await gate.send(request)
            }
        )
        let code = try await session.beginDeviceAuthorization()
        let completion = Task {
            try await session.completeDeviceAuthorization(code)
        }
        await gate.waitUntilPollRequested()

        completion.cancel()
        await gate.releasePoll()

        do {
            _ = try await completion.value
            XCTFail("Cancellation must supersede a late device-poll failure")
        } catch is CancellationError {
        }
    }

    func testDeviceAuthorizationSurfacesExplicitDenial() async throws {
        let state = ResponseQueue(responses: [
            .json(200, [
                "device_auth_id": "device-1",
                "user_code": "ABCD-1234",
                "interval": "0",
            ]),
            .json(403, [
                "error": "access_denied",
                "error_description": "The user declined the request.",
            ]),
        ])
        let session = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(),
            httpClient: IXClosureHTTPClient { request in
                try await state.next(request)
            }
        )
        let code = try await session.beginDeviceAuthorization()

        do {
            _ = try await session.completeDeviceAuthorization(code)
            XCTFail("An explicit denial must not be polled until expiry")
        } catch IXCodexError.deviceAuthorizationDenied {
        }
    }

    func testNewDeviceAuthorizationInvalidatesThePreviousCode() async throws {
        let state = ResponseQueue(responses: [
            .json(200, [
                "device_auth_id": "device-old",
                "user_code": "OLD-1234",
                "interval": "0",
            ]),
            .json(200, [
                "device_auth_id": "device-new",
                "user_code": "NEW-1234",
                "interval": "0",
            ]),
        ])
        let session = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(),
            httpClient: IXClosureHTTPClient { request in
                try await state.next(request)
            }
        )
        let oldCode = try await session.beginDeviceAuthorization()
        _ = try await session.beginDeviceAuthorization()

        do {
            _ = try await session.completeDeviceAuthorization(oldCode)
            XCTFail("A replaced device code must stop immediately")
        } catch is CancellationError {
        }
        let requests = await state.requests
        XCTAssertEqual(requests.count, 2)
    }
}

actor SuspendedCredentialStore: IXCodexCredentialStoring {
    private var bundle: IXCodexAuthBundle?
    private var shouldSuspendLoad: Bool
    private var shouldSuspendSave: Bool
    private var shouldFailLoad: Bool
    private var shouldFailDelete: Bool
    private var loadStarted = false
    private var saveStarted = false
    private var loadStartContinuation: CheckedContinuation<Void, Never>?
    private var saveStartContinuation: CheckedContinuation<Void, Never>?
    private var loadReleaseContinuation: CheckedContinuation<Void, Never>?
    private var saveReleaseContinuation: CheckedContinuation<Void, Never>?

    init(
        bundle: IXCodexAuthBundle?,
        suspendLoadOnce: Bool = false,
        suspendSaveOnce: Bool = false,
        failLoadOnce: Bool = false,
        failDeleteOnce: Bool = false
    ) {
        self.bundle = bundle
        shouldSuspendLoad = suspendLoadOnce
        shouldSuspendSave = suspendSaveOnce
        shouldFailLoad = failLoadOnce
        shouldFailDelete = failDeleteOnce
    }

    func load() async throws -> IXCodexAuthBundle? {
        if shouldSuspendLoad {
            shouldSuspendLoad = false
            loadStarted = true
            loadStartContinuation?.resume()
            loadStartContinuation = nil
            await withCheckedContinuation { loadReleaseContinuation = $0 }
        }
        if shouldFailLoad {
            shouldFailLoad = false
            throw CredentialStoreFailure.loadFailed
        }
        return bundle
    }

    func save(_ bundle: IXCodexAuthBundle) async {
        if shouldSuspendSave {
            shouldSuspendSave = false
            saveStarted = true
            saveStartContinuation?.resume()
            saveStartContinuation = nil
            await withCheckedContinuation { saveReleaseContinuation = $0 }
        }
        self.bundle = bundle
    }

    func delete() throws {
        if shouldFailDelete {
            shouldFailDelete = false
            throw CredentialStoreFailure.deleteFailed
        }
        bundle = nil
    }

    func waitUntilLoadStarted() async {
        guard !loadStarted else { return }
        await withCheckedContinuation { loadStartContinuation = $0 }
    }

    func waitUntilSaveStarted() async {
        guard !saveStarted else { return }
        await withCheckedContinuation { saveStartContinuation = $0 }
    }

    func releaseLoad() {
        loadReleaseContinuation?.resume()
        loadReleaseContinuation = nil
    }

    func releaseSave() {
        saveReleaseContinuation?.resume()
        saveReleaseContinuation = nil
    }

    func currentBundle() -> IXCodexAuthBundle? {
        bundle
    }
}

private enum CredentialStoreFailure: Error {
    case loadFailed
    case deleteFailed
}

actor SuspendedHTTPResponse {
    private let response: IXHTTPResponse
    private var releaseContinuation: CheckedContinuation<Void, Never>?
    private var requestContinuation: CheckedContinuation<Void, Never>?
    private(set) var requestCount = 0

    init(response: IXHTTPResponse) {
        self.response = response
    }

    func send(_ request: URLRequest) async throws -> IXHTTPResponse {
        requestCount += 1
        requestContinuation?.resume()
        requestContinuation = nil
        await withCheckedContinuation { releaseContinuation = $0 }
        return response
    }

    func waitUntilRequested() async {
        guard requestCount == 0 else { return }
        await withCheckedContinuation { requestContinuation = $0 }
    }

    func release() {
        releaseContinuation?.resume()
        releaseContinuation = nil
    }
}

private enum HTTPFailure: Error {
    case lateFailure
}

actor SuspendedHTTPFailure {
    private var requested = false
    private var requestContinuation: CheckedContinuation<Void, Never>?
    private var releaseContinuation: CheckedContinuation<Void, Never>?

    func send(_ request: URLRequest) async throws -> IXHTTPResponse {
        requested = true
        requestContinuation?.resume()
        requestContinuation = nil
        await withCheckedContinuation { releaseContinuation = $0 }
        throw HTTPFailure.lateFailure
    }

    func waitUntilRequested() async {
        guard !requested else { return }
        await withCheckedContinuation { requestContinuation = $0 }
    }

    func release() {
        releaseContinuation?.resume()
        releaseContinuation = nil
    }
}

actor DeviceAuthorizationFailureGate {
    private var requestCount = 0
    private var pollContinuation: CheckedContinuation<Void, Never>?
    private var releaseContinuation: CheckedContinuation<Void, Never>?

    func send(_ request: URLRequest) async throws -> IXHTTPResponse {
        requestCount += 1
        if requestCount == 1 {
            return .json(200, [
                "device_auth_id": "device-cancel",
                "user_code": "CANCEL-1",
                "interval": "0",
            ])
        }
        pollContinuation?.resume()
        pollContinuation = nil
        await withCheckedContinuation { releaseContinuation = $0 }
        throw HTTPFailure.lateFailure
    }

    func waitUntilPollRequested() async {
        guard requestCount > 1 else {
            await withCheckedContinuation { pollContinuation = $0 }
            return
        }
    }

    func releasePoll() {
        releaseContinuation?.resume()
        releaseContinuation = nil
    }
}

actor ResponseQueue {
    private var responses: [IXHTTPResponse]
    private(set) var requests: [URLRequest] = []

    init(responses: [IXHTTPResponse]) {
        self.responses = responses
    }

    func next(_ request: URLRequest) throws -> IXHTTPResponse {
        requests.append(request)
        guard !responses.isEmpty else {
            throw IXCodexError.invalidResponse("unexpected request")
        }
        return responses.removeFirst()
    }
}

extension IXHTTPResponse {
    static func json(_ statusCode: Int, _ object: Any) -> IXHTTPResponse {
        IXHTTPResponse(
            statusCode: statusCode,
            headers: ["content-type": "application/json"],
            body: try! JSONSerialization.data(withJSONObject: object)
        )
    }
}
