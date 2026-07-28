import Foundation

#if os(iOS)
import AVFAudio
import IntelligenceXCodex

actor IXRealtimeAppleAudioSession {
    static let shared = IXRealtimeAppleAudioSession()

    private var activeOwnerID: UUID?

    func activate(ownerID: UUID) async throws {
        let session = AVAudioSession.sharedInstance()
        let isAllowed: Bool
        switch session.recordPermission {
        case .granted:
            isAllowed = true
        case .denied:
            isAllowed = false
        case .undetermined:
            isAllowed = await Self.requestRecordPermission()
        @unknown default:
            isAllowed = false
        }

        guard isAllowed else {
            throw IXCodexError.invalidResponse(
                "Microphone access is off. Allow microphone access for this app in Settings."
            )
        }

        try session.setCategory(
            .playAndRecord,
            mode: .voiceChat,
            options: [.defaultToSpeaker, .allowBluetoothHFP]
        )
        try session.setActive(true)
        activeOwnerID = ownerID
    }

    /// AVAudioSession deliberately invokes its permission callback on a TCC
    /// queue under Mac Catalyst. Keeping the callback outside the @MainActor
    /// executor prevents Swift 6 from asserting that TCC is the main queue.
    nonisolated private static func requestRecordPermission() async -> Bool {
        await withCheckedContinuation { continuation in
            AVAudioSession.sharedInstance().requestRecordPermission { granted in
                continuation.resume(returning: granted)
            }
        }
    }

    func deactivate(ownerID: UUID) {
        guard activeOwnerID == ownerID else { return }
        try? AVAudioSession.sharedInstance().setActive(
            false,
            options: .notifyOthersOnDeactivation
        )
        activeOwnerID = nil
    }
}
#else
actor IXRealtimeAppleAudioSession {
    static let shared = IXRealtimeAppleAudioSession()

    func activate(ownerID: UUID) async throws {}
    func deactivate(ownerID: UUID) {}
}
#endif
