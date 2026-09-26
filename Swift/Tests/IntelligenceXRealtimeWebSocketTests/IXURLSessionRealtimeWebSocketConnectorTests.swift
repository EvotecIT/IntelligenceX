import Foundation
import XCTest
@testable import IntelligenceXRealtimeWebSocket

final class IXURLSessionRealtimeWebSocketConnectorTests: XCTestCase {
    func testDefaultSessionConfigurationKeepsConnectionPolicy() {
        let first = IXURLSessionRealtimeWebSocketConnector.defaultSessionConfiguration()
        let second = IXURLSessionRealtimeWebSocketConnector.defaultSessionConfiguration()

        XCTAssertTrue(first.waitsForConnectivity)
        XCTAssertEqual(first.timeoutIntervalForRequest, 30)
        XCTAssertFalse(first === second)
    }

    func testInjectedSessionIsReusedAndNeverInvalidatedByClose() async throws {
        let observer = SessionInvalidationObserver()
        let configuration = IXURLSessionRealtimeWebSocketConnector
            .defaultSessionConfiguration()
        configuration.waitsForConnectivity = false
        let session = URLSession(
            configuration: configuration,
            delegate: observer,
            delegateQueue: nil
        )
        defer { session.invalidateAndCancel() }
        let connector = IXURLSessionRealtimeWebSocketConnector(session: session)
        // Loopback discard port: nothing leaves the machine and nothing answers.
        let url = try XCTUnwrap(URL(string: "ws://127.0.0.1:9/v1/realtime"))

        let first = try await connector.connect(to: url, protocols: ["realtime"])
        await first.close()
        let second = try await connector.connect(to: url, protocols: ["realtime"])
        await second.close()
        try await Task.sleep(for: .milliseconds(100))

        XCTAssertEqual(observer.createdTaskCount, 2)
        XCTAssertFalse(observer.didInvalidate)
    }
}

private final class SessionInvalidationObserver: NSObject, URLSessionTaskDelegate,
    @unchecked Sendable {
    private let lock = NSLock()
    private var invalidated = false
    private var createdTasks = 0

    var didInvalidate: Bool {
        lock.withLock { invalidated }
    }

    var createdTaskCount: Int {
        lock.withLock { createdTasks }
    }

    func urlSession(_ session: URLSession, didCreateTask task: URLSessionTask) {
        lock.withLock { createdTasks += 1 }
    }

    func urlSession(_ session: URLSession, didBecomeInvalidWithError error: Error?) {
        lock.withLock { invalidated = true }
    }
}
