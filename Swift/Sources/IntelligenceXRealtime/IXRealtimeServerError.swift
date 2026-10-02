import Foundation
import IntelligenceXCodex

/// Structured service error metadata. `eventID` identifies the client event
/// that failed, rather than the server's error notification itself.
public struct IXRealtimeServerError: Sendable, Equatable {
    public let type: String?
    public let code: String?
    public let message: String?
    public let eventID: String?

    init(_ value: IXJSONValue) {
        type = value["type"]?.stringValue
        code = value["code"]?.stringValue
        message = [value["message"]?.stringValue, code, type]
            .compactMap { $0?.trimmingCharacters(in: .whitespacesAndNewlines) }
            .first { !$0.isEmpty }
        eventID = value["event_id"]?.stringValue
    }

    static func payload(in raw: IXJSONValue) -> IXJSONValue? {
        let payload: IXJSONValue?
        switch raw["type"]?.stringValue {
        case "error", "conversation.item.input_audio_transcription.failed":
            payload = raw["error"]
        case "response.done" where raw["response"]?["status"]?.stringValue == "failed":
            payload = raw["response"]?["status_details"]?["error"]
        default:
            return nil
        }
        guard let payload, payload.objectValue != nil else { return nil }
        return payload
    }
}

extension IXRealtimeEvent {
    /// Preserves error codes and request correlation without requiring clients
    /// to parse the wire payload or classify localized message text. Includes
    /// failed responses and input transcription failures; their typed events
    /// retain their response or input identity.
    public var serverError: IXRealtimeServerError? {
        guard let error = IXRealtimeServerError.payload(in: raw) else { return nil }
        return IXRealtimeServerError(error)
    }
}

extension IXRealtimeClientEvent {
    /// Cancels only the named response. The caller-supplied event identifier
    /// allows a client to correlate an error when completion races cancellation.
    public static func cancelResponse(responseID: String, eventID: String) -> IXJSONValue {
        .object([
            "type": .string("response.cancel"),
            "response_id": .string(responseID),
            "event_id": .string(eventID),
        ])
    }
}
