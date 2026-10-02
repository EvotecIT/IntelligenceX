import Foundation
import IntelligenceXCodex
import IntelligenceXRealtime
import XCTest

final class IXRealtimeServerErrorTests: XCTestCase {
    func testFailedResponseExposesErrorWithoutLosingCompletionIdentity() throws {
        let event = try IXRealtimeEvent(data: Data(#"{"type":"response.done","response":{"id":"reply-1","status":"failed","status_details":{"error":{"type":"insufficient_quota","code":"insufficient_quota","message":"Usage limit reached"}}}}"#.utf8))
        XCTAssertEqual(event.errorMessage, "Usage limit reached")
        XCTAssertEqual(event.serverError?.code, "insufficient_quota")
        guard case .responseCompleted(let id, _) = event.serverEvent else {
            return XCTFail("Failure must still release the identified response")
        }
        XCTAssertEqual(id, "reply-1")
    }

    func testMessageLessFailuresFallBackToCodeThenType() throws {
        for eventType in ["response.done", "error", "conversation.item.input_audio_transcription.failed"] {
            for error in [["code": "server_error", "type": "server"], ["message": "  ", "type": "server"]] {
                var payload: [String: Any] = ["type": eventType, "item_id": "input-1", "error": error]
                if eventType == "response.done" {
                    payload["response"] = ["id": "reply-1", "status": "failed", "status_details": ["error": error]]
                }
                let event = try IXRealtimeEvent(data: JSONSerialization.data(withJSONObject: payload))
                XCTAssertEqual(event.errorMessage, error["code"] ?? error["type"])
                XCTAssertEqual(event.serverError?.message, event.errorMessage)
                if eventType == "response.done" {
                    guard case .responseCompleted(let id, _) = event.serverEvent else {
                        return XCTFail("Failed response must retain completion identity")
                    }
                    XCTAssertEqual(id, "reply-1")
                }
            }
        }
    }

    func testTranscriptionFailurePreservesStructuredErrorAndInputIdentity() throws {
        let event = try IXRealtimeEvent(data: Data(#"{"type":"conversation.item.input_audio_transcription.failed","item_id":"input-1","error":{"code":"server_error","message":"Transcription unavailable"}}"#.utf8))
        XCTAssertEqual(event.serverError?.code, "server_error")
        XCTAssertEqual(event.serverEvent, .inputTranscriptionFailed(itemID: "input-1", message: "Transcription unavailable"))
    }

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
