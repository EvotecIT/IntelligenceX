import IntelligenceXCodex

/// Serializes explicit Realtime response requests so a client never creates a
/// second response while the server still owns the first one.
public struct IXRealtimeResponseCoordinator: Sendable, Equatable {
    public enum Request: Sendable, Equatable {
        case standard
        case withoutTools

        public var event: IXJSONValue {
            switch self {
            case .standard:
                IXRealtimeClientEvent.createResponse
            case .withoutTools:
                IXRealtimeClientEvent.createResponseWithoutTools
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
        guard isBusy else {
            isAwaitingResponseCreated = true
            return .send(request)
        }
        if pendingRequests.last != request {
            pendingRequests.append(request)
        }
        return .queued
    }

    public mutating func didObserveResponse(_ responseID: String) {
        isAwaitingResponseCreated = false
        activeResponseIDs.insert(responseID)
    }

    public mutating func didFinishResponse(_ responseID: String) {
        isAwaitingResponseCreated = false
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

    public mutating func reset() {
        isAwaitingResponseCreated = false
        activeResponseIDs.removeAll()
        pendingRequests.removeAll()
    }
}
