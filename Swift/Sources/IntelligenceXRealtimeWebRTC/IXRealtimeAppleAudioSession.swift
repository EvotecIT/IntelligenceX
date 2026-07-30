import Foundation

#if os(iOS)
import AVFAudio
import IntelligenceXCodex

actor IXRealtimeAppleAudioSession {
    static let shared = IXRealtimeAppleAudioSession()

    private var activeOwnerIDs: Set<UUID> = []

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
        activeOwnerIDs.insert(ownerID)
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
        guard activeOwnerIDs.remove(ownerID) != nil,
              activeOwnerIDs.isEmpty else { return }
        try? AVAudioSession.sharedInstance().setActive(
            false,
            options: .notifyOthersOnDeactivation
        )
    }

    /// Reapplies the shared voice-chat configuration after an interruption,
    /// route change, or media-services reset without changing ownership.
    func recover(ownerID: UUID) throws {
        guard activeOwnerIDs.contains(ownerID) else { return }
        let session = AVAudioSession.sharedInstance()
        try session.setCategory(
            .playAndRecord,
            mode: .voiceChat,
            options: [.defaultToSpeaker, .allowBluetoothHFP]
        )
        try session.setActive(true)
    }
}
#else
actor IXRealtimeAppleAudioSession {
    static let shared = IXRealtimeAppleAudioSession()

    func activate(ownerID: UUID) async throws {}
    func deactivate(ownerID: UUID) {}
    func recover(ownerID: UUID) throws {}
}
#endif
