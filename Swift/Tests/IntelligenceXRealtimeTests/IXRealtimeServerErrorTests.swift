import Foundation
import IntelligenceXCodex
import IntelligenceXRealtime
import XCTest

final class IXRealtimeServerErrorTests: XCTestCase {
    func testErrorRetainsClientCorrelationAndLegacyMessage() throws {
        let event = try IXRealtimeEvent(data: Data(#"{"type":"error","event_id":"server-notification","error":{"type":"invalid_request_error","code":"response_cancel_not_active","message":"Already complete","event_id":"client-cancel"}}"#.utf8))
        let error = try XCTUnwrap(event.serverError)
        XCTAssertEqual(error.type, "invalid_request_error")
        XCTAssertEqual(error.code, "response_cancel_not_active")
        XCTAssertEqual(error.eventID, "client-cancel")
        XCTAssertEqual(error.message, "Already complete")
        XCTAssertEqual(event.serverEvent, .error(message: "Already complete"))
    }

    func testPartialAndNonErrorEventsDoNotInventCorrelation() throws {
        let partial = try IXRealtimeEvent(data: Data(#"{"type":"error","event_id":"server-only","error":{"message":"Failed"}}"#.utf8))
        XCTAssertNil(partial.serverError?.eventID)
        XCTAssertNil(partial.serverError?.code)
        let unrelated = try IXRealtimeEvent(data: Data(#"{"type":"response.done","response":{"error":{"code":"ignored"}}}"#.utf8))
        XCTAssertNil(unrelated.serverError)
        let malformed = try IXRealtimeEvent(data: Data(#"{"type":"error","error":"invalid"}"#.utf8))
        XCTAssertNil(malformed.serverError)
    }

    func testScopedCancellationEncodesResponseAndClientEvent() throws {
        let value = try IXJSONValue.decode(
            IXRealtimeClientEvent.cancelResponse(responseID: "reply-1", eventID: "cancel-1").encodedData()
        )
        XCTAssertEqual(value["type"]?.stringValue, "response.cancel")
        XCTAssertEqual(value["response_id"]?.stringValue, "reply-1")
        XCTAssertEqual(value["event_id"]?.stringValue, "cancel-1")
        XCTAssertNil(IXRealtimeClientEvent.cancelResponse["response_id"])
    }
}
