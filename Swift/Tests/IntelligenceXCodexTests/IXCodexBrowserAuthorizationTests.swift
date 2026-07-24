import Foundation
@testable import IntelligenceXCodex
import XCTest

final class IXCodexBrowserAuthorizationTests: XCTestCase {
    private let now = Date(timeIntervalSince1970: 1_750_000_000)

    func testBrowserAuthorizationUsesCodexPKCEAndLocalhostContract() async throws {
        let session = makeSession(ResponseQueue(responses: []))
        let redirect = try XCTUnwrap(URL(string: "http://localhost:1455/auth/callback"))

        let authorization = try await session.beginBrowserAuthorization(redirectURL: redirect)
        let components = try XCTUnwrap(
            URLComponents(url: authorization.authorizationURL, resolvingAgainstBaseURL: false)
        )
        let query = Dictionary(
            uniqueKeysWithValues: try XCTUnwrap(components.queryItems).compactMap { item in
                item.value.map { (item.name, $0) }
            }
        )

        XCTAssertEqual(components.scheme, "https")
        XCTAssertEqual(components.host, "auth.openai.com")
        XCTAssertEqual(components.path, "/oauth/authorize")
        XCTAssertEqual(query["response_type"], "code")
        XCTAssertEqual(query["client_id"], "app_EMoamEEZ73f0CkXaXp7hrann")
        XCTAssertEqual(query["redirect_uri"], redirect.absoluteString)
        XCTAssertEqual(query["code_challenge_method"], "S256")
        XCTAssertEqual(query["code_challenge"], "DwBzhbb51LfusnSGBa_hqYSgo7-j8BTQnip4TOnlzRo")
        XCTAssertEqual(query["state"], "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")
        XCTAssertEqual(query["originator"], "casaray")
        XCTAssertEqual(query["codex_cli_simplified_flow"], "true")
        XCTAssertEqual(authorization.expiresAt, now.addingTimeInterval(10 * 60))
    }

    func testBrowserCallbackExchangesCodeAndPersistsRotatableBundle() async throws {
        let state = ResponseQueue(responses: [
            .json(200, [
                "access_token": "browser-access",
                "refresh_token": "browser-refresh",
                "id_token": jwt(payload: [
                    "email": "home@example.com",
                    "https://api.openai.com/auth": [
                        "chatgpt_account_id": "account-browser",
                        "chatgpt_plan_type": "plus",
                    ],
                ]),
                "expires_in": 3600,
            ]),
        ])
        let credentialStore = IXMemoryCodexCredentialStore()
        let session = makeSession(state, credentialStore: credentialStore)
        let redirect = try XCTUnwrap(URL(string: "http://localhost:1455/auth/callback"))
        let authorization = try await session.beginBrowserAuthorization(redirectURL: redirect)
        let callback = try XCTUnwrap(URL(
            string: "\(redirect.absoluteString)?code=authorization-code&state=\(authorization.state)"
        ))

        let bundle = try await session.completeBrowserAuthorization(
            authorization,
            callbackURL: callback
        )

        XCTAssertEqual(bundle.accessToken, "browser-access")
        XCTAssertEqual(bundle.refreshToken, "browser-refresh")
        XCTAssertEqual(bundle.accountID, "account-browser")
        let storedBundle = await credentialStore.load()
        XCTAssertEqual(storedBundle, bundle)
        let requests = await state.requests
        XCTAssertEqual(requests.count, 1)
        let body = try XCTUnwrap(String(data: try XCTUnwrap(requests.first?.httpBody), encoding: .utf8))
        XCTAssertTrue(body.contains("grant_type=authorization_code"))
        XCTAssertTrue(body.contains("code=authorization-code"))
        XCTAssertTrue(body.contains("redirect_uri=http%3A%2F%2Flocalhost%3A1455%2Fauth%2Fcallback"))
        XCTAssertTrue(body.contains("code_verifier=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"))
    }

    func testBrowserCallbackRejectsMismatchedStateBeforeTokenExchange() async throws {
        let state = ResponseQueue(responses: [])
        let credentialStore = IXMemoryCodexCredentialStore()
        let session = makeSession(state, credentialStore: credentialStore)
        let redirect = try XCTUnwrap(URL(string: "http://localhost:1455/auth/callback"))
        let authorization = try await session.beginBrowserAuthorization(redirectURL: redirect)
        let callback = try XCTUnwrap(URL(
            string: "\(redirect.absoluteString)?code=authorization-code&state=attacker-state"
        ))

        do {
            _ = try await session.completeBrowserAuthorization(
                authorization,
                callbackURL: callback
            )
            XCTFail("A mismatched OAuth state must be rejected")
        } catch let error as IXCodexError {
            XCTAssertEqual(error, .browserAuthorizationStateMismatch)
        }

        let requests = await state.requests
        let storedBundle = await credentialStore.load()
        XCTAssertTrue(requests.isEmpty)
        XCTAssertNil(storedBundle)
    }

    func testBrowserErrorCallbackRequiresMatchingState() async throws {
        let state = ResponseQueue(responses: [])
        let session = makeSession(state)
        let redirect = try XCTUnwrap(URL(string: "http://localhost:1455/auth/callback"))
        let authorization = try await session.beginBrowserAuthorization(redirectURL: redirect)
        let callback = try XCTUnwrap(URL(
            string: "\(redirect.absoluteString)?error=access_denied&state=attacker-state"
        ))

        do {
            _ = try await session.completeBrowserAuthorization(
                authorization,
                callbackURL: callback
            )
            XCTFail("An OAuth error callback with the wrong state must be rejected")
        } catch let error as IXCodexError {
            XCTAssertEqual(error, .browserAuthorizationStateMismatch)
        }
        let requests = await state.requests
        XCTAssertTrue(requests.isEmpty)
    }

    func testBrowserCallbackRejectsDuplicateStateWithoutCrashing() async throws {
        let state = ResponseQueue(responses: [])
        let session = makeSession(state)
        let redirect = try XCTUnwrap(URL(string: "http://localhost:1455/auth/callback"))
        let authorization = try await session.beginBrowserAuthorization(redirectURL: redirect)
        let callback = try XCTUnwrap(URL(
            string: "\(redirect.absoluteString)?code=value&state=\(authorization.state)&state=\(authorization.state)"
        ))

        do {
            _ = try await session.completeBrowserAuthorization(
                authorization,
                callbackURL: callback
            )
            XCTFail("Duplicate OAuth state parameters must be rejected")
        } catch let error as IXCodexError {
            XCTAssertEqual(error, .browserAuthorizationStateMismatch)
        }
        let requests = await state.requests
        XCTAssertTrue(requests.isEmpty)
    }

    func testBrowserCallbackRejectsDuplicateCodeWithoutCrashing() async throws {
        let state = ResponseQueue(responses: [])
        let session = makeSession(state)
        let redirect = try XCTUnwrap(URL(string: "http://localhost:1455/auth/callback"))
        let authorization = try await session.beginBrowserAuthorization(redirectURL: redirect)
        let callback = try XCTUnwrap(URL(
            string: "\(redirect.absoluteString)?code=one&code=two&state=\(authorization.state)"
        ))

        do {
            _ = try await session.completeBrowserAuthorization(
                authorization,
                callbackURL: callback
            )
            XCTFail("Duplicate OAuth code parameters must be rejected")
        } catch let error as IXCodexError {
            guard case .invalidBrowserCallback = error else {
                return XCTFail("Unexpected error: \(error)")
            }
        }
        let requests = await state.requests
        XCTAssertTrue(requests.isEmpty)
    }

    func testBrowserAuthorizationRejectsUnregisteredCallbackPort() async throws {
        let session = makeSession(ResponseQueue(responses: []))
        let redirect = try XCTUnwrap(URL(string: "http://localhost:9000/auth/callback"))

        do {
            _ = try await session.beginBrowserAuthorization(redirectURL: redirect)
            XCTFail("Only Codex allow-listed callback ports may be used")
        } catch let error as IXCodexError {
            guard case .invalidBrowserCallback = error else {
                return XCTFail("Unexpected error: \(error)")
            }
        }
    }

    private func makeSession(
        _ state: ResponseQueue,
        credentialStore: IXMemoryCodexCredentialStore = IXMemoryCodexCredentialStore()
    ) -> IXCodexAuthSession {
        let fixedNow = now
        return IXCodexAuthSession(
            configuration: IXCodexConfiguration(originator: "casaray"),
            credentialStore: credentialStore,
            httpClient: IXClosureHTTPClient { request in try await state.next(request) },
            now: { fixedNow },
            randomBytes: { [UInt8](repeating: 0, count: $0) }
        )
    }

    private func jwt(payload: [String: Any]) -> String {
        let header = try! JSONSerialization.data(withJSONObject: ["alg": "none"])
        let payloadData = try! JSONSerialization.data(withJSONObject: payload)
        return "\(base64URL(header)).\(base64URL(payloadData)).signature"
    }

    private func base64URL(_ data: Data) -> String {
        data.base64EncodedString()
            .replacingOccurrences(of: "+", with: "-")
            .replacingOccurrences(of: "/", with: "_")
            .replacingOccurrences(of: "=", with: "")
    }
}
