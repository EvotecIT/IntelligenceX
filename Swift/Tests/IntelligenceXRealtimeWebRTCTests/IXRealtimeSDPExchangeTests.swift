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

    @MainActor
    func testCarPlayAudioProfileCanBeSelectedExplicitly() {
        let session = IXRealtimeWebRTCSession(
            secret: .init(
                value: "test-secret",
                expiresAt: .distantFuture,
                model: "test-model"
            ),
            audioSessionProfile: .carPlayConversation,
            onEvent: { _ in }
        )

        XCTAssertFalse(session.isReady)
    }

    @MainActor
    func testCanceledConnectTearsDownMicrophonePeerAndAudioOwnership() async throws {
        let exchange = SuspendedSDPExchange()
        let initialOwnerCount = await IXRealtimeAppleAudioSession.shared
            .activeOwnerCount
        var states: [IXRealtimeConnectionState] = []
        let session = IXRealtimeWebRTCSession(
            secret: .init(
                value: "test-secret",
                expiresAt: .distantFuture,
                model: "test-model"
            ),
            exchange: exchange,
            onEvent: { _ in },
            onState: { states.append($0) }
        )
        let connection = Task { try await session.connect() }
        await exchange.waitUntilStarted()

        connection.cancel()
        await exchange.release()

        do {
            try await connection.value
            XCTFail("Canceled WebRTC setup must not leave a live connection")
        } catch is CancellationError {
        }
        for _ in 0..<100 {
            if await IXRealtimeAppleAudioSession.shared.activeOwnerCount ==
                initialOwnerCount {
                break
            }
            try await Task.sleep(for: .milliseconds(5))
        }

        XCTAssertFalse(session.isReady)
        XCTAssertFalse(session.isMicrophoneEnabled)
        XCTAssertEqual(states.last, .idle)
        let finalOwnerCount = await IXRealtimeAppleAudioSession.shared
            .activeOwnerCount
        XCTAssertEqual(finalOwnerCount, initialOwnerCount)
    }

    @MainActor
    func testAlreadyCanceledConnectCannotLeakAudioOwnership() async throws {
        let start = ManualStartGate()
        let initialOwnerCount = await IXRealtimeAppleAudioSession.shared
            .activeOwnerCount
        let session = IXRealtimeWebRTCSession(
            secret: .init(
                value: "test-secret",
                expiresAt: .distantFuture,
                model: "test-model"
            ),
            onEvent: { _ in }
        )
        let connection = Task {
            await start.wait()
            try await session.connect()
        }
        await start.waitUntilStarted()
        connection.cancel()
        await start.release()

        do {
            try await connection.value
            XCTFail("An already-canceled setup must not acquire audio")
        } catch is CancellationError {
        }
        for _ in 0..<100 {
            if await IXRealtimeAppleAudioSession.shared.activeOwnerCount ==
                initialOwnerCount {
                break
            }
            try await Task.sleep(for: .milliseconds(5))
        }
        let finalOwnerCount = await IXRealtimeAppleAudioSession.shared
            .activeOwnerCount
        XCTAssertEqual(finalOwnerCount, initialOwnerCount)
        XCTAssertFalse(session.isReady)
    }

    @MainActor
    func testCanceledConnectPrefersCancellationOverLateSDPFailure()
        async throws {
        let exchange = SuspendedFailingSDPExchange()
        let initialOwnerCount = await IXRealtimeAppleAudioSession.shared
            .activeOwnerCount
        var states: [IXRealtimeConnectionState] = []
        let session = IXRealtimeWebRTCSession(
            secret: .init(
                value: "test-secret",
                expiresAt: .distantFuture,
                model: "test-model"
            ),
            exchange: exchange,
            onEvent: { _ in },
            onState: { states.append($0) }
        )
        let connection = Task { try await session.connect() }
        await exchange.waitUntilStarted()

        connection.cancel()
        await exchange.release()

        do {
            try await connection.value
            XCTFail("Cancellation must supersede a late SDP failure")
        } catch is CancellationError {
        }
        for _ in 0..<100 {
            if await IXRealtimeAppleAudioSession.shared.activeOwnerCount ==
                initialOwnerCount {
                break
            }
            try await Task.sleep(for: .milliseconds(5))
        }
        XCTAssertFalse(states.contains { state in
            if case .failed = state { return true }
            return false
        })
        let finalOwnerCount = await IXRealtimeAppleAudioSession.shared
            .activeOwnerCount
        XCTAssertEqual(finalOwnerCount, initialOwnerCount)
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

private actor SuspendedSDPExchange: IXRealtimeSDPExchanging {
    private var started = false
    private var startContinuation: CheckedContinuation<Void, Never>?
    private var releaseContinuation: CheckedContinuation<Void, Never>?

    func exchange(
        offer: String,
        secret: IXRealtimeClientSecret
    ) async -> String {
        started = true
        startContinuation?.resume()
        startContinuation = nil
        await withCheckedContinuation { releaseContinuation = $0 }
        return "v=0\r\n"
    }

    func waitUntilStarted() async {
        guard !started else { return }
        await withCheckedContinuation { startContinuation = $0 }
    }

    func release() {
        releaseContinuation?.resume()
        releaseContinuation = nil
    }
}

private actor ManualStartGate {
    private var started = false
    private var startedContinuation: CheckedContinuation<Void, Never>?
    private var releaseContinuation: CheckedContinuation<Void, Never>?

    func wait() async {
        started = true
        startedContinuation?.resume()
        startedContinuation = nil
        await withCheckedContinuation { releaseContinuation = $0 }
    }

    func waitUntilStarted() async {
        guard !started else { return }
        await withCheckedContinuation { startedContinuation = $0 }
    }

    func release() {
        releaseContinuation?.resume()
        releaseContinuation = nil
    }
}

private actor SuspendedFailingSDPExchange: IXRealtimeSDPExchanging {
    private var started = false
    private var startContinuation: CheckedContinuation<Void, Never>?
    private var releaseContinuation: CheckedContinuation<Void, Never>?

    func exchange(
        offer: String,
        secret: IXRealtimeClientSecret
    ) async throws -> String {
        started = true
        startContinuation?.resume()
        startContinuation = nil
        await withCheckedContinuation { releaseContinuation = $0 }
        throw RealtimeSetupFailure.lateFailure
    }

    func waitUntilStarted() async {
        guard !started else { return }
        await withCheckedContinuation { startContinuation = $0 }
    }

    func release() {
        releaseContinuation?.resume()
        releaseContinuation = nil
    }
}

private enum RealtimeSetupFailure: Error {
    case lateFailure
}
