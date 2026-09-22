@testable import IntelligenceXRealtime
import IntelligenceXCodex
import XCTest

final class IXRealtimeResponseCoordinatorTests: XCTestCase {
    func testScopedResponseOffersOnlyTheRequestedTools() {
        let tool = IXCodexToolDefinition(name: "read_state", description: "Read state",
                                        parameters: .object(["type": .string("object")]))
        let event = IXRealtimeResponseCoordinator.Request.withTools([tool]).event
        XCTAssertEqual(event["type"]?.stringValue, "response.create")
        XCTAssertEqual(event["response"]?["tool_choice"]?.stringValue, "auto")
        XCTAssertEqual(event["response"]?["tools"]?.arrayValue, [.object([
            "type": .string("function"), "name": .string("read_state"),
            "description": .string("Read state"), "parameters": tool.parameters,
        ])])
        let empty = IXRealtimeClientEvent.createResponse(tools: [])
        XCTAssertEqual(empty["response"]?["tool_choice"]?.stringValue, "none")
    }

    func testSupersedingTurnDropsOnlyQueuedContinuations() {
        var coordinator = IXRealtimeResponseCoordinator()
        XCTAssertEqual(coordinator.submit(.standard), .send(.standard))
        XCTAssertEqual(coordinator.submit(.withoutTools), .queued)
        coordinator.discardPendingRequests()
        XCTAssertTrue(coordinator.isAwaitingResponseCreated)
        coordinator.didObserveResponse("old")
        XCTAssertEqual(coordinator.submit(.standard), .queued)
        XCTAssertNil(coordinator.takePendingRequestIfReady())
        coordinator.didFinishResponse("old")
        XCTAssertEqual(coordinator.takePendingRequestIfReady(), .standard)
    }

    func testRejectedInputDeletionPreservesItsExactIdentity() {
        let event = IXRealtimeClientEvent.deleteConversationItem(itemID: "input-echo")
        XCTAssertEqual(event["type"]?.stringValue, "conversation.item.delete")
        XCTAssertEqual(event["item_id"]?.stringValue, "input-echo")
        XCTAssertNil(event["response_id"])
    }

    func testSubmissionCannotOvertakeAQueuedRequestAfterCompletion() {
        var coordinator = IXRealtimeResponseCoordinator()
        coordinator.didObserveResponse("old")
        XCTAssertEqual(coordinator.submit(.withoutTools), .queued)
        coordinator.didFinishResponse("old")
        XCTAssertEqual(coordinator.submit(.standard), .send(.withoutTools))
        XCTAssertEqual(coordinator.pendingRequests, [.standard])
        XCTAssertNil(coordinator.takePendingRequestIfReady())
        coordinator.didObserveResponse("continuation")
        coordinator.didFinishResponse("continuation")
        XCTAssertEqual(coordinator.takePendingRequestIfReady(), .standard)
    }

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

    func testRejectedCreateSettlesOnlyTheAwaitingRequest() {
        var coordinator = IXRealtimeResponseCoordinator()

        XCTAssertEqual(coordinator.submit(.standard), .send(.standard))
        XCTAssertEqual(coordinator.submit(.withoutTools), .queued)
        coordinator.didRejectAwaitingRequest()

        XCTAssertEqual(
            coordinator.takePendingRequestIfReady(),
            .withoutTools
        )
        XCTAssertTrue(coordinator.isAwaitingResponseCreated)
    }
}
