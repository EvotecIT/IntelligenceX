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
        message = value["message"]?.stringValue
        eventID = value["event_id"]?.stringValue
    }
}

extension IXRealtimeEvent {
    /// Preserves error codes and request correlation without requiring clients
    /// to parse the wire payload or classify localized message text.
    public var serverError: IXRealtimeServerError? {
        guard type == "error", let error = raw["error"],
              error.objectValue != nil else { return nil }
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
