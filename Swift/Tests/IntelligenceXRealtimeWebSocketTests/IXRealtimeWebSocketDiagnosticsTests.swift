import Foundation
import IntelligenceXCodex
import IntelligenceXRealtime
@testable import IntelligenceXRealtimeWebSocket
import XCTest

@MainActor final class IXRealtimeWebSocketDiagnosticsTests: XCTestCase {
    func testUnexpectedTransportCancellationIsReportedWithOriginalCloseEvidence() async throws {
        for error: any Error in [URLError(.cancelled), CancellationError()] {
            let connection = DiagnosticConnection(error: error, failReceive: true)
            let failed = expectation(description: "transport failed")
            let session = makeSession(connection) { state in
                if case .failed = state { failed.fulfill() }
            }
            try await session.connect()
            await fulfillment(of: [failed], timeout: 1)
            let failure = try XCTUnwrap(session.lastFailure)
            XCTAssertEqual(failure.operation, .receive)
            XCTAssertEqual(failure.domain, error is CancellationError ? .cancellation : .url)
            XCTAssertFalse(failure.taskWasCancelled)
            XCTAssertEqual(failure.closeCode, 1006)
            await session.disconnect()
            XCTAssertEqual(session.lastFailure, failure)
        }
    }

    func testSendFailurePreservesNumericCodeWithoutRetainingPrivateErrorContent() async throws {
        let error = NSError(domain: NSURLErrorDomain, code: NSURLErrorNetworkConnectionLost,
                            userInfo: [NSURLErrorFailingURLStringErrorKey: "https://private.invalid/token",
                                       NSLocalizedDescriptionKey: "private server text"])
        let connection = DiagnosticConnection(error: error, failReceive: false)
        let session = makeSession(connection)
        try await session.connect()
        await connection.failNextSend()
        do {
            try await session.send(IXRealtimeClientEvent.clearInputAudioBuffer)
            XCTFail("Send should fail")
        } catch {
            XCTAssertEqual((error as NSError).code, NSURLErrorNetworkConnectionLost)
        }
        XCTAssertEqual(session.lastFailure?.operation, .send)
        XCTAssertEqual(session.lastFailure?.domain, .url)
        XCTAssertEqual(session.lastFailure?.code, NSURLErrorNetworkConnectionLost)
        XCTAssertFalse(String(reflecting: session.lastFailure).contains("private"))
        await session.disconnect()
    }

    func testIntentionalDisconnectDoesNotCreateTransportFailure() async throws {
        let connection = DiagnosticConnection(error: URLError(.cancelled), failReceive: false)
        let session = makeSession(connection)
        try await session.connect()
        await session.disconnect()
        await Task.yield()
        XCTAssertNil(session.lastFailure)
        XCTAssertEqual(session.state, .idle)
    }

    private func makeSession(
        _ connection: DiagnosticConnection,
        onState: @escaping IXRealtimeWebSocketSession.StateHandler = { _ in }
    ) -> IXRealtimeWebSocketSession {
        .init(secret: .init(value: "test", expiresAt: .distantFuture, model: "test"),
              connector: DiagnosticConnector(connection: connection), onEvent: { _ in }, onState: onState)
    }
}

private struct DiagnosticConnector: IXRealtimeWebSocketConnecting {
    let connection: DiagnosticConnection
    func connect(to url: URL, protocols: [String]) async throws -> any IXRealtimeWebSocketConnection {
        connection
    }
}

private actor DiagnosticConnection: IXRealtimeWebSocketConnection {
    let error: any Error
    let failReceive: Bool
    var failSend = false
    var closed = false
    init(error: any Error, failReceive: Bool) { self.error = error; self.failReceive = failReceive }
    func failNextSend() { failSend = true }
    func send(_ data: Data) throws { if failSend { throw error } }
    func receive() async throws -> Data {
        if failReceive { throw error }
        while !closed { try await Task.sleep(for: .milliseconds(10)) }
        throw URLError(.cancelled)
    }
    func close() { closed = true }
    func failureDiagnostics(for error: any Error, operation: IXRealtimeWebSocketFailure.Operation,
                            taskWasCancelled: Bool) -> IXRealtimeWebSocketFailure {
        .init(error: error, operation: operation, taskWasCancelled: taskWasCancelled,
              closeCode: closed ? 1001 : 1006)
    }
}
