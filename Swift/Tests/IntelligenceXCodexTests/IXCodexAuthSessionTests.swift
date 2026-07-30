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
    private var loadStarted = false
    private var saveStarted = false
    private var loadStartContinuation: CheckedContinuation<Void, Never>?
    private var saveStartContinuation: CheckedContinuation<Void, Never>?
    private var loadReleaseContinuation: CheckedContinuation<Void, Never>?
    private var saveReleaseContinuation: CheckedContinuation<Void, Never>?

    init(
        bundle: IXCodexAuthBundle?,
        suspendLoadOnce: Bool = false,
        suspendSaveOnce: Bool = false
    ) {
        self.bundle = bundle
        shouldSuspendLoad = suspendLoadOnce
        shouldSuspendSave = suspendSaveOnce
    }

    func load() async -> IXCodexAuthBundle? {
        if shouldSuspendLoad {
            shouldSuspendLoad = false
            loadStarted = true
            loadStartContinuation?.resume()
            loadStartContinuation = nil
            await withCheckedContinuation { loadReleaseContinuation = $0 }
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

    func delete() {
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
