import Foundation

#if os(iOS)
@preconcurrency import AVFAudio
import IntelligenceXCodex

actor IXRealtimeAppleAudioSession {
    static let shared = IXRealtimeAppleAudioSession()

    private var profilesByOwnerID: [UUID: IXRealtimeAudioSessionProfile] = [:]
    private var ownerActivationOrder: [UUID] = []

    func activate(
        ownerID: UUID,
        profile: IXRealtimeAudioSessionProfile
    ) async throws {
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

        try Self.configureVoiceSession(profile: profile)
        profilesByOwnerID[ownerID] = profile
        ownerActivationOrder.removeAll(where: { $0 == ownerID })
        ownerActivationOrder.append(ownerID)
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

    func deactivate(ownerID: UUID) async {
        guard profilesByOwnerID.removeValue(forKey: ownerID) != nil else {
            return
        }
        ownerActivationOrder.removeAll(where: { $0 == ownerID })
        guard let remainingOwnerID = ownerActivationOrder.last,
              let remainingProfile = profilesByOwnerID[remainingOwnerID]
        else {
            try? AVAudioSession.sharedInstance().setActive(
                false,
                options: .notifyOthersOnDeactivation
            )
            return
        }
        try? Self.configureVoiceSession(profile: remainingProfile)
    }

    /// Reapplies the shared voice-chat configuration after an interruption,
    /// route change, or media-services reset without changing ownership.
    func recover(ownerID: UUID) async throws {
        guard ownerActivationOrder.last == ownerID,
              let profile = profilesByOwnerID[ownerID] else { return }
        try Self.configureVoiceSession(profile: profile)
    }

    /// Keep category changes synchronous on this actor's serial executor. An
    /// awaited detached configuration would make the actor reentrant and let
    /// a superseded recovery finish after a newer owner became active.
    nonisolated private static func configureVoiceSession(
        profile: IXRealtimeAudioSessionProfile
    ) throws {
        let session = AVAudioSession.sharedInstance()
        switch profile {
        case .voiceConversation:
            try session.setCategory(
                .playAndRecord,
                mode: .voiceChat,
                options: [.defaultToSpeaker, .allowBluetoothHFP]
            )
        case .carPlayConversation:
            try session.setCategory(
                .playAndRecord,
                mode: .default,
                options: []
            )
        }
        try session.setActive(true)
    }
}
#else
actor IXRealtimeAppleAudioSession {
    static let shared = IXRealtimeAppleAudioSession()

    func activate(
        ownerID: UUID,
        profile: IXRealtimeAudioSessionProfile
    ) async throws {}
    func deactivate(ownerID: UUID) async {}
    func recover(ownerID: UUID) async throws {}
}
#endif
