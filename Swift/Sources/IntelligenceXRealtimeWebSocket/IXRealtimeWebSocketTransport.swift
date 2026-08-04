import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

public protocol IXRealtimeWebSocketConnection: Sendable {
    func send(_ data: Data) async throws
    func receive() async throws -> Data
    func close() async
}

public protocol IXRealtimeWebSocketConnecting: Sendable {
    func connect(
        to url: URL,
        protocols: [String]
    ) async throws -> any IXRealtimeWebSocketConnection
}

public struct IXURLSessionRealtimeWebSocketConnector:
    IXRealtimeWebSocketConnecting, Sendable {
    public init() {}

    public func connect(
        to url: URL,
        protocols: [String]
    ) async throws -> any IXRealtimeWebSocketConnection {
        let connection = IXURLSessionRealtimeWebSocketConnection(
            url: url,
            protocols: protocols
        )
        await connection.resume()
        return connection
    }
}

private actor IXURLSessionRealtimeWebSocketConnection:
    IXRealtimeWebSocketConnection {
    private let session: URLSession
    private let task: URLSessionWebSocketTask

    init(url: URL, protocols: [String]) {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.waitsForConnectivity = true
        configuration.timeoutIntervalForRequest = 30
        session = URLSession(configuration: configuration)
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
        session.invalidateAndCancel()
    }
}

public enum IXRealtimeWebSocketError: Error, Equatable, Sendable {
    case invalidUTF8Event
    case unsupportedMessage
    case notConnected
}
