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
            isAllowed = await withCheckedContinuation { continuation in
                session.requestRecordPermission { granted in
                    continuation.resume(returning: granted)
                }
            }
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
