import IntelligenceXCodex

/// Serializes explicit Realtime response requests so a client never creates a
/// second response while the server still owns the first one.
public struct IXRealtimeResponseCoordinator: Sendable, Equatable {
    public enum Request: Sendable, Equatable {
        case standard
        case withoutTools
        case withTools([IXCodexToolDefinition])

        public var event: IXJSONValue {
            switch self {
            case .standard:
                IXRealtimeClientEvent.createResponse
            case .withoutTools:
                IXRealtimeClientEvent.createResponseWithoutTools
            case .withTools(let tools):
                IXRealtimeClientEvent.createResponse(tools: tools)
            }
        }
    }

    public enum Submission: Sendable, Equatable {
        case send(Request)
        case queued
    }

    public private(set) var isAwaitingResponseCreated = false
    public private(set) var activeResponseIDs: Set<String> = []
    public private(set) var pendingRequests: [Request] = []

    public init() {}

    public var isBusy: Bool {
        isAwaitingResponseCreated || !activeResponseIDs.isEmpty
    }

    public mutating func submit(_ request: Request) -> Submission {
        if pendingRequests.last != request {
            pendingRequests.append(request)
        }
        guard !isBusy else { return .queued }
        isAwaitingResponseCreated = true
        return .send(pendingRequests.removeFirst())
    }

    public mutating func didObserveResponse(_ responseID: String) {
        isAwaitingResponseCreated = false
        activeResponseIDs.insert(responseID)
    }

    public mutating func didFinishResponse(_ responseID: String) {
        guard activeResponseIDs.contains(responseID) else { return }
        activeResponseIDs.remove(responseID)
    }

    public mutating func takePendingRequestIfReady() -> Request? {
        guard !isBusy, !pendingRequests.isEmpty else {
            return nil
        }
        isAwaitingResponseCreated = true
        return pendingRequests.removeFirst()
    }

    public mutating func didFailToSend() {
        isAwaitingResponseCreated = false
    }

    /// Settles a response.create that reached the server but was rejected
    /// before response.created supplied an identity.
    public mutating func didRejectAwaitingRequest() {
        isAwaitingResponseCreated = false
    }

    /// Discards queued continuations when a newer user turn supersedes them.
    /// The current server response remains tracked until it actually finishes.
    public mutating func discardPendingRequests() {
        pendingRequests.removeAll()
    }

    public mutating func reset() {
        isAwaitingResponseCreated = false
        activeResponseIDs.removeAll()
        pendingRequests.removeAll()
    }
}
