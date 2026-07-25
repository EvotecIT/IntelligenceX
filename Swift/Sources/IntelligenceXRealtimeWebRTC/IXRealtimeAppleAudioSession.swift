#if os(iOS)
import AVFAudio
import IntelligenceXCodex

@MainActor
enum IXRealtimeAppleAudioSession {
    static func activate() async throws {
        let session = AVAudioSession.sharedInstance()
        let isAllowed: Bool
        switch session.recordPermission {
        case .granted:
            isAllowed = true
        case .denied:
            isAllowed = false
        case .undetermined:
            isAllowed = await requestRecordPermission()
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

    static func deactivate() {
        try? AVAudioSession.sharedInstance().setActive(
            false,
            options: .notifyOthersOnDeactivation
        )
    }
}
#else
@MainActor
enum IXRealtimeAppleAudioSession {
    static func activate() async throws {}
    static func deactivate() {}
}
#endif
