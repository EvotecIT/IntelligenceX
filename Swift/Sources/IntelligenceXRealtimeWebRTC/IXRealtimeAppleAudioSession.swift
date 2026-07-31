import Foundation

#if os(iOS)
@preconcurrency import AVFAudio
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

        try await Self.configureVoiceSession()
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
    func recover(ownerID: UUID) async throws {
        guard activeOwnerIDs.contains(ownerID) else { return }
        try await Self.configureVoiceSession()
    }

    /// AVAudioSession category changes can synchronously wait on its XPC
    /// service for hundreds of milliseconds. Keep that system wait off the UI
    /// actor even when recovery was initiated by a main-queue notification.
    nonisolated private static func configureVoiceSession() async throws {
        try await Task.detached(priority: .userInitiated) {
            let session = AVAudioSession.sharedInstance()
            try session.setCategory(
                .playAndRecord,
                mode: .voiceChat,
                options: [.defaultToSpeaker, .allowBluetoothHFP]
            )
            try session.setActive(true)
        }.value
    }
}
#else
actor IXRealtimeAppleAudioSession {
    static let shared = IXRealtimeAppleAudioSession()

    func activate(ownerID: UUID) async throws {}
    func deactivate(ownerID: UUID) {}
    func recover(ownerID: UUID) async throws {}
}
#endif
