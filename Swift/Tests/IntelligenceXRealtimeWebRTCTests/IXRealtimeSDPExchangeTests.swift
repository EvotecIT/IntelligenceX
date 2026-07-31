import Foundation
import IntelligenceXRealtime
import XCTest
@testable import IntelligenceXRealtimeWebRTC

final class IXRealtimeSDPExchangeTests: XCTestCase {
    func testConnectionStateGateCoalescesDuplicateReadyCallbacks() {
        var gate = IXRealtimeConnectionStateGate()

        XCTAssertTrue(gate.accepts(.connecting))
        XCTAssertFalse(gate.accepts(.connecting))
        XCTAssertTrue(gate.accepts(.connected))
        XCTAssertFalse(gate.accepts(.connected))
        XCTAssertTrue(gate.accepts(.disconnected))
        XCTAssertFalse(gate.accepts(.disconnected))
        XCTAssertTrue(gate.accepts(.connected))
    }

    @MainActor
    func testReceivedAudioCanBeMutedLocallyBeforeServerAcknowledgement() {
        let session = IXRealtimeWebRTCSession(
            secret: .init(
                value: "test-secret",
                expiresAt: .distantFuture,
                model: "test-model"
            ),
            onEvent: { _ in }
        )

        XCTAssertTrue(session.isOutputPlaybackEnabled)
        session.setOutputPlaybackEnabled(false)
        XCTAssertFalse(session.isOutputPlaybackEnabled)
        session.setOutputPlaybackEnabled(true)
        XCTAssertTrue(session.isOutputPlaybackEnabled)
    }

    func testSDPIsEncodedAsAFormFieldInsteadOfAFileUpload() throws {
        let body = IXRealtimeMultipartForm.sdpBody(
            "v=0\r\na=example",
            boundary: "test-boundary"
        )
        let value = try XCTUnwrap(String(data: body, encoding: .utf8))

        XCTAssertTrue(value.contains("Content-Disposition: form-data; name=\"sdp\"\r\n"))
        XCTAssertFalse(value.contains("filename="))
        XCTAssertTrue(value.contains("Content-Type: application/sdp\r\n\r\nv=0\r\na=example"))
        XCTAssertTrue(value.hasSuffix("\r\n--test-boundary--\r\n"))
    }
}
