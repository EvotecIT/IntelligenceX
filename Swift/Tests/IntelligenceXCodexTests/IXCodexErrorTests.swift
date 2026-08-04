import IntelligenceXCodex
import XCTest

final class IXCodexErrorTests: XCTestCase {
    func testUsageLimitClassificationCoversSubscriptionAndCreditFailures() {
        let errors: [IXCodexError] = [
            .requestFailed(status: 429, message: "Too many requests"),
            .requestFailed(status: 402, message: "Payment required"),
            .requestFailed(status: 403, message: "usage cap reached"),
            .requestFailed(status: 400, message: "insufficient_quota"),
            .requestFailed(status: 400, message: "Quota exhausted"),
            .requestFailed(status: 400, message: "Credit balance is empty"),
        ]

        XCTAssertTrue(errors.allSatisfy(\.isUsageLimitReached))
    }

    func testUnrelatedRequestFailureIsNotAUsageLimit() {
        XCTAssertFalse(
            IXCodexError.requestFailed(
                status: 500,
                message: "Internal server error"
            ).isUsageLimitReached
        )
    }
}
