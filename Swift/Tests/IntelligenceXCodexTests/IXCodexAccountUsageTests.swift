import Foundation
@testable import IntelligenceXCodex
import XCTest

final class IXCodexAccountUsageTests: XCTestCase {
    func testAccountUsageIsAccountScopedAndParsesCurrentLimits() async throws {
        let recorder = AccountUsageRequestRecorder()
        let configuration = IXCodexConfiguration(
            accountUsageURL: URL(string: "https://example.test/wham/usage")!
        )
        let authSession = IXCodexAuthSession(
            configuration: configuration,
            credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
                accessToken: "access",
                refreshToken: "refresh",
                expiresAt: .distantFuture,
                accountID: "account-123"
            ))
        )
        let client = IXCodexClient(
            configuration: configuration,
            authSession: authSession,
            httpClient: IXClosureHTTPClient { request in
                await recorder.record(request)
                return .json(200, [
                    "plan_type": "plus",
                    "rate_limit": [
                        "allowed": true,
                        "limit_reached": false,
                        "primary_window": [
                            "used_percent": 42,
                            "limit_window_seconds": 18_000,
                            "reset_at": 2_000_000_000,
                        ],
                        "secondary_window": [
                            "used_percent": 5,
                            "limit_window_seconds": 604_800,
                            "reset_at": 2_000_100_000,
                        ],
                    ],
                    "credits": [
                        "has_credits": true,
                        "unlimited": false,
                        "balance": "12",
                    ],
                    "rate_limit_reset_credits": [
                        "available_count": 2,
                    ],
                ])
            }
        )

        let usage = try await client.accountUsage()

        XCTAssertEqual(usage.plan, "plus")
        XCTAssertEqual(usage.primaryRateLimit?.primaryWindow?.usedPercent, 42)
        XCTAssertEqual(
            usage.primaryRateLimit?.primaryWindow?.duration,
            18_000
        )
        XCTAssertEqual(
            usage.primaryRateLimit?.secondaryWindow?.remainingPercent,
            95
        )
        XCTAssertEqual(usage.credits?.balance, "12")
        XCTAssertEqual(usage.availableResetCredits, 2)
        let recordedRequest = await recorder.request
        let request = try XCTUnwrap(recordedRequest)
        XCTAssertEqual(
            request.value(forHTTPHeaderField: "ChatGPT-Account-ID"),
            "account-123"
        )
        XCTAssertEqual(
            request.value(forHTTPHeaderField: "Authorization"),
            "Bearer access"
        )
    }

    func testAuthorizationSnapshotDoesNotExposeTokens() async throws {
        let expiresAt = Date(timeIntervalSince1970: 2_000_000_000)
        let authSession = IXCodexAuthSession(
            credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
                accessToken: "access",
                refreshToken: "refresh",
                expiresAt: expiresAt,
                accountID: "account-123"
            )),
            now: { Date(timeIntervalSince1970: 1_000_000_000) }
        )

        let snapshot = try await authSession.authorizationSnapshot()

        XCTAssertEqual(snapshot.account?.id, "account-123")
        XCTAssertEqual(snapshot.accessTokenExpiresAt, expiresAt)
        XCTAssertFalse(snapshot.accessTokenNeedsRefresh)
    }
}

private actor AccountUsageRequestRecorder {
    private(set) var request: URLRequest?

    func record(_ request: URLRequest) {
        self.request = request
    }
}
