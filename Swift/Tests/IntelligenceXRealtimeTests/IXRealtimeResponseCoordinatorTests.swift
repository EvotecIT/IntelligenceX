@testable import IntelligenceXRealtime
import XCTest

final class IXRealtimeResponseCoordinatorTests: XCTestCase {
    func testQueuesUntilTheServerFinishesItsActiveResponse() {
        var coordinator = IXRealtimeResponseCoordinator()

        XCTAssertEqual(coordinator.submit(.standard), .send(.standard))
        XCTAssertEqual(coordinator.submit(.withoutTools), .queued)
        coordinator.didObserveResponse("response-1")

        XCTAssertNil(coordinator.takePendingRequestIfReady())

        coordinator.didFinishResponse("response-1")
        XCTAssertEqual(
            coordinator.takePendingRequestIfReady(),
            .withoutTools
        )
        XCTAssertTrue(coordinator.isAwaitingResponseCreated)
    }

    func testCoalescesOnlyAdjacentDuplicateRequests() {
        var coordinator = IXRealtimeResponseCoordinator()

        XCTAssertEqual(coordinator.submit(.standard), .send(.standard))
        XCTAssertEqual(coordinator.submit(.standard), .queued)
        XCTAssertEqual(coordinator.submit(.standard), .queued)
        XCTAssertEqual(coordinator.submit(.withoutTools), .queued)
        XCTAssertEqual(coordinator.submit(.standard), .queued)

        XCTAssertEqual(
            coordinator.pendingRequests,
            [.standard, .withoutTools, .standard]
        )
    }

    func testFailedSendAndResetReleaseSessionOwnedState() {
        var coordinator = IXRealtimeResponseCoordinator()

        XCTAssertEqual(coordinator.submit(.standard), .send(.standard))
        coordinator.didFailToSend()
        XCTAssertFalse(coordinator.isBusy)

        coordinator.didObserveResponse("old-session")
        XCTAssertEqual(coordinator.submit(.withoutTools), .queued)
        coordinator.reset()

        XCTAssertFalse(coordinator.isBusy)
        XCTAssertTrue(coordinator.pendingRequests.isEmpty)
    }
}
