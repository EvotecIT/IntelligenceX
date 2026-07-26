import Foundation
import IntelligenceXCodex
import IntelligenceXRealtime
@testable import IntelligenceXRealtimeWebSocket
import XCTest

@MainActor
final class IXRealtimeWebSocketSessionTests: XCTestCase {
    func testSessionUsesEphemeralProtocolAndModelEndpoint() async throws {
        let connection = RealtimeWebSocketConnection()
        let connector = RealtimeWebSocketConnector(connection: connection)
        let session = IXRealtimeWebSocketSession(
            secret: IXRealtimeClientSecret(
                value: "ek_watch",
                expiresAt: .distantFuture,
                model: "gpt-realtime-2.1"
            ),
            connector: connector,
            onEvent: { _ in }
        )

        try await session.connect()

        let request = await connector.request
        XCTAssertEqual(
            request?.url.query(),
            "model=gpt-realtime-2.1"
        )
        XCTAssertEqual(
            request?.protocols,
            ["realtime", "openai-insecure-api-key.ek_watch"]
        )
        let sent = await connection.sent
        let firstEvent = try IXJSONValue.decode(try XCTUnwrap(sent.first))
        XCTAssertEqual(firstEvent["type"]?.stringValue, "input_audio_buffer.clear")
        XCTAssertTrue(session.isReady)

        await session.disconnect()
    }

    func testSessionDecodesIncomingAudioEvent() async throws {
        let pcm = Data([0, 1, 2, 3])
        let connection = RealtimeWebSocketConnection(messages: [
            try JSONEncoder().encode(IXJSONValue.object([
                "type": .string("response.output_audio.delta"),
                "response_id": .string("response-1"),
                "item_id": .string("item-1"),
                "delta": .string(pcm.base64EncodedString()),
            ])),
        ])
        let connector = RealtimeWebSocketConnector(connection: connection)
        let eventExpectation = expectation(description: "audio event")
        let received = RealtimeEventRecorder()
        let session = IXRealtimeWebSocketSession(
            secret: .init(
                value: "ek_watch",
                expiresAt: .distantFuture,
                model: "gpt-realtime-2.1"
            ),
            connector: connector,
            onEvent: { event in
                await received.record(event)
                eventExpectation.fulfill()
            }
        )

        try await session.connect()
        await fulfillment(of: [eventExpectation], timeout: 1)

        let event = await received.event
        XCTAssertEqual(event?.outputAudioData, pcm)
        XCTAssertEqual(event?.responseID, "response-1")
        XCTAssertEqual(event?.itemID, "item-1")
        await session.disconnect()
    }

    func testSessionClosesTransportWhenReceiveFails() async throws {
        let connection = RealtimeWebSocketConnection(
            receiveError: IXRealtimeWebSocketError.unsupportedMessage
        )
        let failureExpectation = expectation(description: "connection failed")
        let connector = RealtimeWebSocketConnector(connection: connection)
        let session = IXRealtimeWebSocketSession(
            secret: .init(
                value: "ek_watch",
                expiresAt: .distantFuture,
                model: "gpt-realtime-2.1"
            ),
            connector: connector,
            onEvent: { _ in },
            onState: { state in
                if case .failed = state { failureExpectation.fulfill() }
            }
        )

        try await session.connect()
        await fulfillment(of: [failureExpectation], timeout: 1)

        let isClosed = await connection.isClosed
        XCTAssertTrue(isClosed)
        XCTAssertFalse(session.isReady)
    }
}

private actor RealtimeWebSocketConnection: IXRealtimeWebSocketConnection {
    private(set) var sent: [Data] = []
    private(set) var isClosed = false
    private var messages: [Data]
    private let receiveError: IXRealtimeWebSocketError?

    init(
        messages: [Data] = [],
        receiveError: IXRealtimeWebSocketError? = nil
    ) {
        self.messages = messages
        self.receiveError = receiveError
    }

    func send(_ data: Data) {
        sent.append(data)
    }

    func receive() async throws -> Data {
        if let receiveError { throw receiveError }
        while messages.isEmpty {
            try await Task.sleep(for: .milliseconds(10))
        }
        return messages.removeFirst()
    }

    func close() { isClosed = true }
}

private actor RealtimeWebSocketConnector: IXRealtimeWebSocketConnecting {
    struct Request: Sendable {
        var url: URL
        var protocols: [String]
    }

    private let connection: RealtimeWebSocketConnection
    private(set) var request: Request?

    init(connection: RealtimeWebSocketConnection) {
        self.connection = connection
    }

    func connect(
        to url: URL,
        protocols: [String]
    ) -> any IXRealtimeWebSocketConnection {
        request = Request(url: url, protocols: protocols)
        return connection
    }
}

private actor RealtimeEventRecorder {
    private(set) var event: IXRealtimeEvent?

    func record(_ event: IXRealtimeEvent) {
        self.event = event
    }
}
