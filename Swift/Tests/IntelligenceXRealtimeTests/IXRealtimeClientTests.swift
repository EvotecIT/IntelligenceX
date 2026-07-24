import Foundation
import IntelligenceXCodex
@testable import IntelligenceXRealtime
import XCTest

final class IXRealtimeClientTests: XCTestCase {
    func testClientSecretUsesChatGPTOAuthAndAccountHeaders() async throws {
        let recorder = RealtimeRequestRecorder()
        let auth = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
                accessToken: "oauth-access",
                refreshToken: "refresh",
                expiresAt: .distantFuture,
                accountID: "account-1"
            ))
        )
        let client = IXRealtimeClient(
            authSession: auth,
            httpClient: IXClosureHTTPClient { request in
                await recorder.record(request)
                return .json(200, [
                    "value": "ek_test",
                    "expires_at": 2_000_000_000,
                    "session": ["model": "gpt-realtime-2.1"],
                ])
            }
        )

        let secret = try await client.createClientSecret(options: .init(instructions: "Help with the home."))

        XCTAssertEqual(secret.value, "ek_test")
        let recordedRequest = await recorder.request
        let request = try XCTUnwrap(recordedRequest)
        XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer oauth-access")
        XCTAssertEqual(request.value(forHTTPHeaderField: "ChatGPT-Account-ID"), "account-1")
        let body = try IXJSONValue.decode(try XCTUnwrap(request.httpBody))
        XCTAssertEqual(body["session"]?["model"]?.stringValue, "gpt-realtime-2.1")
        let expectedModalities: [IXJSONValue] = [.string("audio")]
        XCTAssertEqual(body["session"]?["output_modalities"]?.arrayValue, expectedModalities)
    }
}

private actor RealtimeRequestRecorder {
    private(set) var request: URLRequest?

    func record(_ request: URLRequest) {
        self.request = request
    }
}

private extension IXHTTPResponse {
    static func json(_ statusCode: Int, _ object: Any) -> IXHTTPResponse {
        IXHTTPResponse(statusCode: statusCode, body: try! JSONSerialization.data(withJSONObject: object))
    }
}
