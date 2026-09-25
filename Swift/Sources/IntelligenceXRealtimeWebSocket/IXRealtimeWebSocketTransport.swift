import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public protocol IXRealtimeWebSocketConnection: Sendable {
    func send(_ data: Data) async throws
    func receive() async throws -> Data
    func close() async
    /// Capture before closing; local teardown can replace the peer's close code.
    func failureDiagnostics(
        for error: any Error, operation: IXRealtimeWebSocketFailure.Operation,
        taskWasCancelled: Bool
    ) async -> IXRealtimeWebSocketFailure
}

public extension IXRealtimeWebSocketConnection {
    func failureDiagnostics(
        for error: any Error, operation: IXRealtimeWebSocketFailure.Operation,
        taskWasCancelled: Bool
    ) async -> IXRealtimeWebSocketFailure {
        .init(error: error, operation: operation, taskWasCancelled: taskWasCancelled)
    }
}

public protocol IXRealtimeWebSocketConnecting: Sendable {
    func connect(
        to url: URL,
        protocols: [String]
    ) async throws -> any IXRealtimeWebSocketConnection
}

/// Opens Realtime WebSockets with `URLSessionWebSocketTask`.
///
/// By default every connection creates, and later invalidates, its own
/// ephemeral `URLSession`. Apps that connect repeatedly can instead pass one
/// long-lived session so connections share its DNS, TLS session-resumption,
/// and connection-pool state, and so the app can warm that session before the
/// first voice start.
public struct IXURLSessionRealtimeWebSocketConnector:
    IXRealtimeWebSocketConnecting, Sendable {
    private let sharedSession: URLSession?

    /// Creates a connector that uses a new ephemeral session per connection,
    /// configured by `defaultSessionConfiguration()`.
    public init() {
        sharedSession = nil
    }

    /// Creates a connector that opens every WebSocket on `session`.
    ///
    /// The caller owns `session`: closing a connection cancels only its
    /// WebSocket task and never invalidates the shared session. Build it from
    /// `defaultSessionConfiguration()` to keep the default connection policy.
    /// Do not use a background session; WebSocket tasks are not supported
    /// there.
    public init(session: URLSession) {
        sharedSession = session
    }

    /// The configuration used for the per-connection sessions of `init()`:
    /// ephemeral storage, waiting for connectivity, and a 30-second request
    /// timeout. Returns a new instance on every call.
    public static func defaultSessionConfiguration() -> URLSessionConfiguration {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.waitsForConnectivity = true
        configuration.timeoutIntervalForRequest = 30
        return configuration
    }

    public func connect(
        to url: URL,
        protocols: [String]
    ) async throws -> any IXRealtimeWebSocketConnection {
        let connection: IXURLSessionRealtimeWebSocketConnection
        if let sharedSession {
            connection = IXURLSessionRealtimeWebSocketConnection(
                session: sharedSession,
                ownsSession: false,
                url: url,
                protocols: protocols
            )
        } else {
            connection = IXURLSessionRealtimeWebSocketConnection(
                session: URLSession(
                    configuration: Self.defaultSessionConfiguration()
                ),
                ownsSession: true,
                url: url,
                protocols: protocols
            )
        }
        await connection.resume()
        return connection
    }
}

private actor IXURLSessionRealtimeWebSocketConnection:
    IXRealtimeWebSocketConnection {
    private let session: URLSession
    private let ownsSession: Bool
    private let task: URLSessionWebSocketTask

    init(session: URLSession, ownsSession: Bool, url: URL, protocols: [String]) {
        self.session = session
        self.ownsSession = ownsSession
        task = session.webSocketTask(with: url, protocols: protocols)
    }

    func resume() {
        task.resume()
    }

    func send(_ data: Data) async throws {
        guard let text = String(data: data, encoding: .utf8) else {
            throw IXRealtimeWebSocketError.invalidUTF8Event
        }
        try await task.send(.string(text))
    }

    func receive() async throws -> Data {
        switch try await task.receive() {
        case .data(let data):
            return data
        case .string(let text):
            guard let data = text.data(using: .utf8) else {
                throw IXRealtimeWebSocketError.invalidUTF8Event
            }
            return data
        @unknown default:
            throw IXRealtimeWebSocketError.unsupportedMessage
        }
    }

    func close() {
        task.cancel(with: .goingAway, reason: nil)
        if ownsSession {
            session.invalidateAndCancel()
        }
    }

    func failureDiagnostics(
        for error: any Error, operation: IXRealtimeWebSocketFailure.Operation,
        taskWasCancelled: Bool
    ) -> IXRealtimeWebSocketFailure {
        .init(error: error, operation: operation, taskWasCancelled: taskWasCancelled,
              closeCode: task.closeCode == .invalid ? nil : task.closeCode.rawValue)
    }
}

public enum IXRealtimeWebSocketError: Error, Equatable, Sendable {
    case invalidUTF8Event
    case unsupportedMessage
    case notConnected
}
