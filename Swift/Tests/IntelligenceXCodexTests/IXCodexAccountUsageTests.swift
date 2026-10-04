import Foundation
@testable import IntelligenceXCodex
import XCTest

final class IXCodexAccountUsageTests: XCTestCase {
    func testOptionalResetFailurePreservesUsageAndAccountHeaders() async throws {
        let recorder = AccountUsageRequestRecorder()
        let configuration = IXCodexConfiguration(accountUsageURL: URL(string: "https://example.test/wham/usage")!)
        let session = IXCodexAuthSession(configuration: configuration, credentialStore: IXMemoryCodexCredentialStore(bundle: .init(
            accessToken: "access", refreshToken: "refresh", expiresAt: .distantFuture, accountID: "account-123"
        )))
        let client = IXCodexClient(configuration: configuration, authSession: session, httpClient: IXClosureHTTPClient { request in
            await recorder.record(request)
            XCTAssertEqual(request.httpMethod, "GET")
            XCTAssertEqual(request.value(forHTTPHeaderField: "ChatGPT-Account-ID"), "account-123")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer access")
            if request.url?.path.hasSuffix("/history") == true { return .json(200, ["events": []]) }
            if request.url?.path.hasSuffix("/rate-limit-reset-credits") == true { return .json(403, ["error": "private-response"]) }
            return .json(200, ["plan_type": "pro", "rate_limit_reset_credits": ["available_count": 3]])
        })
        let usage = try await client.accountUsage()
        XCTAssertEqual(usage.plan, "pro")
        XCTAssertEqual(usage.availableResetCredits, 3)
        XCTAssertFalse(try XCTUnwrap(usage.resetCredits).detailsAvailable)
        XCTAssertTrue(try XCTUnwrap(usage.resetCredits).historyAvailable)
    }
    func testResetGrantsRetainExactExpiryAndPartialHistoryEvidence() throws {
        let usage = try IXCodexAccountUsage.decode(
            Data(#"{"rate_limit_reset_credits":{"available_count":3,"applicable_available_count":0}}"#.utf8),
            resetDetails: Data(#"{"credits":[{"id":"reset","status":"available","expires_at":"2026-10-05T04:18:41.901758Z"}]}"#.utf8),
            resetHistory: Data(#"{"events":[{"kind":"used","occurred_at":"2026-10-03T22:18:02.243412Z"}],"next_cursor":"more"}"#.utf8)
        )
        let reset = try XCTUnwrap(usage.resetCredits)
        XCTAssertEqual(reset.availableCount, 3)
        XCTAssertEqual(reset.applicableAvailableCount, 0)
        XCTAssertTrue(reset.detailsAvailable)
        XCTAssertEqual(reset.grants.first?.expiresAtRaw, "2026-10-05T04:18:41.901758Z")
        XCTAssertNotNil(reset.grants.first?.expiresAt)
        XCTAssertEqual(reset.history.first?.kind, "used")
        XCTAssertEqual(reset.historyNextCursor, "more")
        let countsOnly = try IXCodexAccountUsage.decode(Data(#"{"rate_limit_reset_credits":{"available_count":0}}"#.utf8))
        XCTAssertFalse(try XCTUnwrap(countsOnly.resetCredits).detailsAvailable)
        XCTAssertNil(try IXCodexAccountUsage.decode(Data("{}".utf8)).resetCredits)
    }
    func testCreditBalanceSupportsNumberAndStringWithoutInventingMissingBalance() throws {
        for balance in [62_500, "62500", 0, "0"] as [Any] {
            let data = try JSONSerialization.data(withJSONObject: [
                "credits": ["balance": balance, "has_credits": true, "unlimited": false]
            ])
            let credits = try XCTUnwrap(IXCodexAccountUsage.decode(data).credits)
            XCTAssertNotNil(credits.balanceAmount)
            XCTAssertEqual(credits.balanceAmount, Decimal(string: String(describing: balance)))
        }
        let unknown = try IXCodexAccountUsage.decode(Data(#"{"credits":{"has_credits":true}}"#.utf8))
        XCTAssertNil(unknown.credits?.balance)
        XCTAssertNil(unknown.credits?.balanceAmount)
        XCTAssertEqual(unknown.credits?.hasCredits, true)
        let unlimited = try IXCodexAccountUsage.decode(Data(#"{"credits":{"unlimited":true}}"#.utf8))
        XCTAssertEqual(unlimited.credits?.isUnlimited, true)
        XCTAssertNil(unlimited.credits?.balanceAmount)
    }

    func testFractionalNumericCreditsPreserveDecimalPresentation() throws {
        for literal in ["0.07", "0.1", "62495.90935", "1e-7"] {
            let data = Data("{\"credits\":{\"balance\":\(literal)}}".utf8)
            let credits = try XCTUnwrap(IXCodexAccountUsage.decode(data).credits)
            let expected = try XCTUnwrap(Decimal(string: literal, locale: Locale(identifier: "en_US_POSIX")))
            XCTAssertEqual(credits.balanceAmount, expected)
            XCTAssertEqual(credits.balance, NSDecimalNumber(decimal: expected).stringValue)
        }
    }

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
